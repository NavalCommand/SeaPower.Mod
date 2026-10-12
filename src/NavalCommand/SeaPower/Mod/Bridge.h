#pragma once
#include <NavalCommand/Error/Api/MakeError.h>
#include <NavalCommand/Gateway/Api/Codec.h>
#include <NavalCommand/Gateway/Api/IProvider.h>
#include <atomic>
#include <condition_variable>
#include <cstring>
#include <deque>
#include <map>
#include <mutex>
#include <set>

namespace NavalCommand::SeaPower::Mod
{
	namespace Wire = Gateway::Api::Wire;

	// Worker-side owned messages only. No Unity objects or callbacks enter here.
	class Bridge final : public Gateway::Api::IProvider
	{
		static constexpr std::size_t MAXIMUM_PENDING = 64;
		static constexpr std::uint32_t MAXIMUM_MESSAGE_BYTES = 16 * 1024 * 1024;
		struct Pending
		{
			Wire::Control Control;
			std::chrono::steady_clock::time_point Deadline;
			std::optional<Wire::Result> Result;
		};
		mutable std::mutex m_Mutex;
		std::condition_variable m_Changed;
		std::shared_ptr<const Wire::Publication> m_Publication;
		std::deque<std::shared_ptr<Pending>> m_Queue;
		std::map<std::string, std::shared_ptr<Pending>> m_Pending;
		std::set<std::string> m_Disconnected;
		std::set<std::string> m_PauseClaimClients;
		std::uint64_t m_NextRequest{};
		bool m_Stopped{};

	public:
		std::shared_ptr<const Wire::Publication> Publication() const override
		{
			std::lock_guard lock{m_Mutex};
			return m_Publication;
		}

		Error::Api::Result<Wire::Result> Apply(const Wire::Control& source, std::chrono::steady_clock::time_point deadline) override
		{
			std::unique_lock lock{m_Mutex};
			while (!m_Queue.empty() && (!m_Pending.contains(m_Queue.front()->Control.request()) || std::chrono::steady_clock::now() >= m_Queue.front()->Deadline))
			{
				m_Queue.pop_front();
			}
			if (m_Stopped || !m_Publication || source.session() != m_Publication->session())
			{
				return std::unexpected(Error::Api::MakeError(Error::Api::ErrorCondition::NotReady));
			}
			if (m_Pending.size() >= MAXIMUM_PENDING)
			{
				return std::unexpected(Error::Api::MakeError(Error::Api::ErrorCondition::ResourceExhausted));
			}
			if (deadline <= std::chrono::steady_clock::now())
			{
				return std::unexpected(Error::Api::MakeError(Error::Api::ErrorCondition::CommunicationError));
			}
			auto pending = std::make_shared<Pending>();
			pending->Control = source;
			pending->Control.set_request(std::to_string(++m_NextRequest));
			pending->Deadline = deadline;
			const auto wallDeadline = std::chrono::system_clock::now() + (deadline - std::chrono::steady_clock::now());
			pending->Control.set_expires_unix_milliseconds(std::chrono::duration_cast<std::chrono::milliseconds>(wallDeadline.time_since_epoch()).count());
			const auto request = pending->Control.request();
			m_Pending.emplace(request, pending);
			m_Queue.push_back(pending);
			m_Changed.wait_until(lock,
								 deadline,
								 [&]
								 {
									 return m_Stopped || pending->Result.has_value();
								 });
			m_Pending.erase(request);
			if (pending->Result)
			{
				return *pending->Result;
			}
			return std::unexpected(Error::Api::MakeError(m_Stopped ? Error::Api::ErrorCondition::NotReady : Error::Api::ErrorCondition::CommunicationError));
		}

		void Disconnected(const std::string& client) override
		{
			std::lock_guard lock{m_Mutex};
			// At most sixteen connected clients. Separate priority channel cannot
			// lose a release when the ordinary control queue is full.
			if (m_PauseClaimClients.erase(client) != 0)
			{
				m_Disconnected.insert(client);
			}
		}

		int PublishView(const std::uint8_t* bytes, std::uint32_t length)
		{
			if ((!bytes && length != 0) || length > MAXIMUM_MESSAGE_BYTES)
			{
				return 2;
			}
			auto publication = std::make_shared<Wire::Publication>();
			if (length != 0 && !publication->ParseFromArray(bytes, static_cast<int>(length)))
			{
				return 2;
			}
			std::set<std::string> scopes;
			for (const auto& view : publication->views())
			{
				if (view.scope().kind_case() == Wire::Scope::KIND_NOT_SET || (view.scope().has_faction() && view.scope().faction().empty()))
				{
					return 2;
				}
				const auto key = view.scope().has_faction() ? "faction:" + view.scope().faction() : "omniscient";
				if (!scopes.insert(key).second)
				{
					return 2;
				}
				for (const auto& entry : view.entries())
				{
					if (view.scope().has_faction() && ((entry.result().has_unit() && entry.result().unit().faction() != view.scope().faction()) ||
													   (entry.result().has_observation() && entry.result().observation().observer_faction() != view.scope().faction())))
					{
						return 2;
					}
				}
			}
			std::lock_guard lock{m_Mutex};
			if (m_Publication && m_Publication->session() != publication->session())
			{
				m_PauseClaimClients.clear();
			}
			m_Publication = std::move(publication);
			return 0;
		}

		int PublishResult(const std::uint8_t* bytes, std::uint32_t length)
		{
			Wire::ControlResult result;
			if (!bytes || length == 0 || length > MAXIMUM_MESSAGE_BYTES || !result.ParseFromArray(bytes, static_cast<int>(length)) || !result.has_result())
			{
				return 2;
			}
			std::lock_guard lock{m_Mutex};
			auto pending = m_Pending.find(result.request());
			if (pending == m_Pending.end())
			{
				return 2;
			}
			pending->second->Result = result.result();
			m_Changed.notify_all();
			return 0;
		}

		int Poll(std::uint8_t* destination, std::uint32_t capacity, std::uint32_t* length)
		{
			if (!destination || !length)
			{
				return 2;
			}
			std::lock_guard lock{m_Mutex};
			*length = 0;
			Wire::Control control;
			const bool release = !m_Disconnected.empty();
			if (release)
			{
				control.set_client(*m_Disconnected.begin());
				control.mutable_disconnected();
			}
			else
			{
				while (!m_Queue.empty() && (!m_Pending.contains(m_Queue.front()->Control.request()) || std::chrono::steady_clock::now() >= m_Queue.front()->Deadline))
				{
					m_Queue.pop_front();
				}
				if (m_Queue.empty())
				{
					return 1;
				}
				control = m_Queue.front()->Control;
			}
			const auto serialized = control.SerializeAsString();
			*length = static_cast<std::uint32_t>(serialized.size());
			if (serialized.size() > capacity)
			{
				return 2;
			}
			std::memcpy(destination, serialized.data(), serialized.size());
			if (release)
			{
				m_Disconnected.erase(m_Disconnected.begin());
			}
			else
			{
				if (control.has_pause() && control.pause())
				{
					m_PauseClaimClients.insert(control.client());
				}
				m_Queue.pop_front();
			}
			return 0;
		}

		void Stop()
		{
			std::lock_guard lock{m_Mutex};
			m_Stopped = true;
			m_Publication.reset();
			m_Changed.notify_all();
		}
	};
}
