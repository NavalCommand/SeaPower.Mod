#pragma once
#include <NavalCommand/Gateway/Api/Codec.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <deque>
#include <map>
#include <mutex>

namespace NavalCommand::SeaPower::Mod
{
	// I/O-side mailbox only. All serialized publications copy into owned domain values.
	class Bridge final : public Gateway::Api::IProvider
	{
		static constexpr uint32_t MAXIMUM_ORDER_BYTES = 65'536;
		static constexpr uint32_t MAXIMUM_SNAPSHOT_BYTES = 1'048'576;
		static constexpr std::size_t MAXIMUM_RETAINED_REQUESTS = 128;
		static constexpr std::size_t MAXIMUM_ID_CHARACTERS = 128;
		static constexpr std::size_t MAXIMUM_QUEUED_COMMANDS = 64;
		static constexpr uint64_t MAXIMUM_OBSERVATION_AGE_VERSIONS = 20;
		static constexpr double MAXIMUM_LATITUDE_DEGREES = 90;
		static constexpr double MAXIMUM_LONGITUDE_DEGREES = 180;
		static constexpr uint32_t CANONICAL_SCHEMA_VERSION = 1;

		std::mutex m_Mutex;
		World::Api::View m_View;
		std::deque<std::string> m_Commands;
		std::map<std::string, Orders::Api::Result> m_Ledger;

	public:
		std::expected<World::Api::View, Gateway::Api::Error> View() override
		{
			std::lock_guard lock(m_Mutex);
			if (!m_View.ready)
			{
				return std::unexpected(Gateway::Api::Error::NotReady);
			}
			return m_View;
		}

		Orders::Api::Result Submit(const Orders::Api::Request& request) override
		{
			std::lock_guard lock(m_Mutex);
			using OrderStage = Orders::Api::Stage;
			auto reject = [&](const char* reason)
			{
				return Orders::Api::Result{request.sessionId, request.requestId, OrderStage::Rejected, reason};
			};
			if (!m_View.ready)
			{
				return reject("provider_not_ready");
			}
			if (request.sessionId != m_View.sessionId)
			{
				return reject("stale_session");
			}
			if (auto retainedResult = m_Ledger.find(request.requestId); retainedResult != m_Ledger.end())
			{
				return retainedResult->second;
			}
			if (request.requestId.empty() || request.requestId.size() > MAXIMUM_ID_CHARACTERS || request.entityId.size() > MAXIMUM_ID_CHARACTERS)
			{
				return reject("invalid_arguments");
			}
			if (m_Ledger.size() >= MAXIMUM_RETAINED_REQUESTS)
			{
				return reject("ledger_full");
			}
			auto result = Orders::Api::Result{request.sessionId, request.requestId, OrderStage::Received, "mailbox_received"};
			if (!std::ranges::any_of(m_View.units, [&](auto& unit)
			{
				return unit.id == request.entityId;
			}))
			{
				result = reject("wrong_side_or_invalid_entity");
			}
			else if (!request.facilityLaunch &&
				(!request.waypoint || !std::isfinite(request.waypoint->latitudeDeg) || !std::isfinite(request.waypoint->longitudeDeg) ||
					std::abs(request.waypoint->latitudeDeg) > MAXIMUM_LATITUDE_DEGREES || std::abs(request.waypoint->longitudeDeg) > MAXIMUM_LONGITUDE_DEGREES))
			{
				result = reject("invalid_arguments");
			}
			else if (request.facilityLaunch &&
				(request.waypoint || request.facilityLaunch->count != 1 || request.facilityLaunch->classKey.empty() || request.facilityLaunch->squadron.empty() ||
					request.facilityLaunch->loadout.empty() || request.facilityLaunch->classKey.size() > MAXIMUM_ID_CHARACTERS ||
					request.facilityLaunch->squadron.size() > MAXIMUM_ID_CHARACTERS || request.facilityLaunch->loadout.size() > MAXIMUM_ID_CHARACTERS))
			{
				result = reject("invalid_arguments");
			}
			else if (request.waypoint && request.waypoint->routeMode != Orders::Api::RouteMode::LegacyIdle && request.waypoint->routeMode != Orders::Api::RouteMode::ReplaceRoute &&
				request.waypoint->routeMode != Orders::Api::RouteMode::AppendWaypoint)
			{
				result = reject("unsupported_route_mode");
			}
			else if (request.basedOnVersion == 0 || request.basedOnVersion > m_View.version || m_View.version - request.basedOnVersion > MAXIMUM_OBSERVATION_AGE_VERSIONS)
			{
				result = reject("stale_observation");
			}
			else if (m_Commands.size() >= MAXIMUM_QUEUED_COMMANDS)
			{
				result = reject("queue_full");
			}
			else
			{
				m_Commands.push_back(Gateway::Api::Encode(request).SerializeAsString());
			}
			m_Ledger[request.requestId] = result;
			return result;
		}

		Orders::Api::Result Status(const std::string& sessionId, const std::string& requestId) override
		{
			std::lock_guard lock(m_Mutex);
			if (sessionId != m_View.sessionId)
			{
				return {sessionId, requestId, Orders::Api::Stage::Rejected, "stale_session"};
			}
			if (auto retainedResult = m_Ledger.find(requestId); retainedResult != m_Ledger.end())
			{
				return retainedResult->second;
			}
			return {sessionId, requestId, Orders::Api::Stage::Unknown, "unknown_or_expired_request"};
		}

		int PublishView(const uint8_t* buffer, uint32_t length)
		{
			Gateway::Api::Wire::WorldSnapshot wireValue;
			if (!buffer || length > MAXIMUM_SNAPSHOT_BYTES || !wireValue.ParseFromArray(buffer, length) || wireValue.session().schema_version() != CANONICAL_SCHEMA_VERSION)
			{
				return 2;
			}
			auto domainView = Gateway::Api::Decode(wireValue);
			std::unique_lock lock(m_Mutex, std::try_to_lock);
			if (!lock)
			{
				return 1;
			}
			if (domainView.sessionId != m_View.sessionId || !domainView.ready)
			{
				m_Commands.clear();
				m_Ledger.clear();
			}
			m_View = std::move(domainView);
			return 0;
		}

		int PublishResult(const uint8_t* buffer, uint32_t length)
		{
			Gateway::Api::Wire::OrderResult wireValue;
			if (!buffer || length > MAXIMUM_ORDER_BYTES || !wireValue.ParseFromArray(buffer, length))
			{
				return 2;
			}
			auto result = Gateway::Api::Decode(wireValue);
			std::unique_lock lock(m_Mutex, std::try_to_lock);
			if (!lock)
			{
				return 1;
			}
			if (result.sessionId != m_View.sessionId)
			{
				return 2;
			}
			if (!m_Ledger.contains(result.requestId))
			{
				return 2;
			}
			m_Ledger[result.requestId] = std::move(result);
			return 0;
		}

		int Poll(uint8_t* buffer, uint32_t capacity, uint32_t* length)
		{
			if (!length || !buffer || capacity > MAXIMUM_ORDER_BYTES)
			{
				return 2;
			}
			*length = 0;
			std::unique_lock lock(m_Mutex, std::try_to_lock);
			if (!lock || m_Commands.empty())
			{
				return 1;
			}
			auto& bytes = m_Commands.front();
			*length = static_cast<uint32_t>(bytes.size());
			if (capacity < bytes.size())
			{
				return 2;
			}
			std::memcpy(buffer, bytes.data(), bytes.size());
			m_Commands.pop_front();
			return 0;
		}
	};
} // namespace NavalCommand::SeaPower::Mod
