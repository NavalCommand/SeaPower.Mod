#pragma once
#include <NavalCommand/Gateway/Api/Codec.h>
#include <NavalCommand/Orders/Api/ExecutionRequest.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <deque>
#include <map>
#include <mutex>

namespace NavalCommand::SeaPower::Mod {
// I/O-side mailbox only. Serialized publications become owned values, never game objects.
class Bridge final : public Gateway::Api::IProvider {
	static constexpr uint32_t MAXIMUM_ORDER_BYTES = 65'536;
	static constexpr uint32_t MAXIMUM_SNAPSHOT_BYTES = 1'048'576;
	static constexpr std::size_t MAXIMUM_RETAINED_REQUESTS = 128;
	static constexpr std::size_t MAXIMUM_ID_CHARACTERS = 128;
	static constexpr std::size_t MAXIMUM_QUEUED_COMMANDS = 64;
	static constexpr uint64_t MAXIMUM_OBSERVATION_AGE_VERSIONS = 20;
	static constexpr double MAXIMUM_LATITUDE_DEGREES = 90;
	static constexpr double MAXIMUM_LONGITUDE_DEGREES = 180;
	static constexpr uint32_t CANONICAL_SCHEMA_VERSION = 4;
	std::mutex m_Mutex;
	Gateway::Api::ProviderObservation m_Observation;
	std::deque<std::string> m_Commands;
	std::map<std::string, Orders::Api::Result> m_Ledger;

public:
	std::expected<Gateway::Api::ProviderObservation, Gateway::Api::Error> View() override {
		std::lock_guard lock(m_Mutex);
		if (!m_Observation.Snapshot.Ready) {
			return std::unexpected(Gateway::Api::Error::NotReady);
		}
		return m_Observation;
	}

	Orders::Api::Result Submit(const Orders::Api::ExecutionRequest &request) override {
		std::lock_guard lock(m_Mutex);
		using namespace Orders::Api;
		const auto &snapshot = m_Observation.Snapshot;
		auto reject = [&](const char *reason) {
			Result result;
			result.SessionId = request.SessionId;
			result.RequestId = request.RequestId;
			result.Observer = snapshot.Observer;
			result.Stage = Stage::Rejected;
			result.Reason = reason;
			return result;
		};
		if (!snapshot.Ready) {
			return reject("provider_not_ready");
		}
		if (request.SessionId != snapshot.SessionId) {
			return reject("stale_session");
		}
		if (request.Observer != snapshot.Observer) {
			return reject("wrong_observer_or_invalid_entity");
		}
		if (auto retained = m_Ledger.find(request.RequestId); retained != m_Ledger.end()) {
			return retained->second;
		}
		if (request.RequestId.empty() || request.RequestId.size() > MAXIMUM_ID_CHARACTERS ||
			request.Observation.Value.empty() || request.Observation.Value.size() > MAXIMUM_ID_CHARACTERS) {
			return reject("invalid_arguments");
		}
		if (m_Ledger.size() >= MAXIMUM_RETAINED_REQUESTS) {
			return reject("ledger_full");
		}
		Result result;
		result.SessionId = request.SessionId;
		result.RequestId = request.RequestId;
		result.Observer = request.Observer;
		result.Stage = Stage::Received;
		result.Reason = "mailbox_received";
		const auto route = std::get_if<RouteCommand>(&request.Payload);
		const auto launch = std::get_if<LaunchAircraft>(&request.Payload);
		const auto overlay = std::ranges::find_if(m_Observation.OrderAvailability, [&](const auto &entry) {
			return entry.Observer == request.Observer && entry.Observation == request.Observation;
		});
		if (!std::ranges::any_of(snapshot.Units, [&](const auto &unit) { return unit.Identity == request.Observation; })) {
			result = reject("wrong_side_or_invalid_entity");
		} else if (overlay == m_Observation.OrderAvailability.end()) {
			result = reject("order_availability_unknown");
		} else if (route && (!std::isfinite(route->Latitude.Value) || !std::isfinite(route->Longitude.Value) ||
			std::abs(route->Latitude.Value) > MAXIMUM_LATITUDE_DEGREES || std::abs(route->Longitude.Value) > MAXIMUM_LONGITUDE_DEGREES)) {
			result = reject("invalid_arguments");
		} else if (launch && (launch->Count != 1 || launch->Aircraft.Type.Value.empty() || launch->Aircraft.Squadron.empty() ||
			launch->Aircraft.Loadout.Value.empty() || launch->Aircraft.Type.Value.size() > MAXIMUM_ID_CHARACTERS ||
			launch->Aircraft.Squadron.size() > MAXIMUM_ID_CHARACTERS || launch->Aircraft.Loadout.Value.size() > MAXIMUM_ID_CHARACTERS)) {
			result = reject("invalid_arguments");
		} else if (route && route->Mode != RouteMode::IdleWaypoint && route->Mode != RouteMode::ReplaceRoute && route->Mode != RouteMode::AppendWaypoint) {
			result = reject("unsupported_route_mode");
		} else if (request.BasedOnVersion == 0 || request.BasedOnVersion > snapshot.Version ||
			snapshot.Version - request.BasedOnVersion > MAXIMUM_OBSERVATION_AGE_VERSIONS) {
			result = reject("stale_observation");
		} else {
			const auto kind = launch ? OrderKind::LaunchAircraft : route->Mode == RouteMode::ReplaceRoute ? OrderKind::ReplaceRoute :
				route->Mode == RouteMode::AppendWaypoint ? OrderKind::AppendRoute : OrderKind::IdleWaypoint;
			auto eligibility = std::ranges::find_if(overlay->Orders, [&](const auto &entry) {
				if (entry.Kind != kind) {
					return false;
				}
				return !launch || (entry.LaunchOption && entry.LaunchOption->Aircraft.Type == launch->Aircraft.Type &&
					entry.LaunchOption->Aircraft.Squadron == launch->Aircraft.Squadron &&
					entry.LaunchOption->Aircraft.Loadout == launch->Aircraft.Loadout);
			});
			if (launch && eligibility == overlay->Orders.end()) {
				// Invalid selectors still reach the authoritative game-thread validator.
				eligibility = std::ranges::find_if(overlay->Orders, [&](const auto &entry) {
					return entry.Kind == kind && entry.Availability == Availability::Executable;
				});
			}
			// This is a mailbox gate only. The game-thread adapter must independently
			// revalidate the current observer, faction control, source mapping and native safety.
			if (eligibility == overlay->Orders.end()) {
				result = reject("order_availability_unknown");
			} else if (eligibility->Availability == Availability::Unsupported) {
				result = reject(eligibility->Reason.empty() ? "unsupported_order" : eligibility->Reason.c_str());
			} else if (eligibility->Availability != Availability::Executable) {
				result = reject(eligibility->Reason.empty() ? "order_availability_unknown" : eligibility->Reason.c_str());
			} else if (m_Commands.size() >= MAXIMUM_QUEUED_COMMANDS) {
				result = reject("queue_full");
			} else {
				m_Commands.push_back(Gateway::Api::Encode(request).SerializeAsString());
			}
		}
		m_Ledger[request.RequestId] = result;
		return result;
	}

	Orders::Api::Result Status(const std::string &sessionId, const std::string &requestId) override {
		std::lock_guard lock(m_Mutex);
		if (sessionId != m_Observation.Snapshot.SessionId) {
			Orders::Api::Result result;
			result.SessionId = sessionId;
			result.RequestId = requestId;
			result.Observer = m_Observation.Snapshot.Observer;
			result.Stage = Orders::Api::Stage::Rejected;
			result.Reason = "stale_session";
			return result;
		}
		if (auto retained = m_Ledger.find(requestId); retained != m_Ledger.end()) {
			return retained->second;
		}
		Orders::Api::Result result;
		result.SessionId = sessionId;
		result.RequestId = requestId;
		result.Observer = m_Observation.Snapshot.Observer;
		result.Stage = Orders::Api::Stage::Unknown;
		result.Reason = "unknown_or_expired_request";
		return result;
	}

	int PublishView(const uint8_t *buffer, uint32_t length) {
		Gateway::Api::Wire::ProviderObservation wireValue;
		if (!buffer || length > MAXIMUM_SNAPSHOT_BYTES || !wireValue.ParseFromArray(buffer, length) || !wireValue.has_snapshot() ||
			wireValue.snapshot().schema_version() != CANONICAL_SCHEMA_VERSION) {
			return 2;
		}
		Gateway::Api::ProviderObservation observation;
		try {
			observation = Gateway::Api::Decode(wireValue);
		} catch (const std::invalid_argument &) {
			return 2;
		}
		std::unique_lock lock(m_Mutex, std::try_to_lock);
		if (!lock) {
			return 1;
		}
		if (observation.Snapshot.SessionId != m_Observation.Snapshot.SessionId ||
			observation.Snapshot.Observer != m_Observation.Snapshot.Observer || !observation.Snapshot.Ready) {
			m_Commands.clear();
			m_Ledger.clear();
		}
		m_Observation = std::move(observation);
		return 0;
	}

	int PublishResult(const uint8_t *buffer, uint32_t length) {
		Gateway::Api::Wire::OrderResult wireValue;
		if (!buffer || length > MAXIMUM_ORDER_BYTES || !wireValue.ParseFromArray(buffer, length)) {
			return 2;
		}
		Orders::Api::Result result;
		try {
			result = Gateway::Api::Decode(wireValue);
		} catch (const std::invalid_argument &) {
			return 2;
		}
		std::unique_lock lock(m_Mutex, std::try_to_lock);
		if (!lock) {
			return 1;
		}
		if (result.SessionId != m_Observation.Snapshot.SessionId || result.Observer != m_Observation.Snapshot.Observer ||
			!m_Ledger.contains(result.RequestId)) {
			return 2;
		}
		m_Ledger[result.RequestId] = std::move(result);
		return 0;
	}

	int Poll(uint8_t *buffer, uint32_t capacity, uint32_t *length) {
		if (!length || !buffer || capacity > MAXIMUM_ORDER_BYTES) {
			return 2;
		}
		*length = 0;
		std::unique_lock lock(m_Mutex, std::try_to_lock);
		if (!lock || m_Commands.empty()) {
			return 1;
		}
		const auto &bytes = m_Commands.front();
		*length = static_cast<uint32_t>(bytes.size());
		if (capacity < bytes.size()) {
			return 2;
		}
		std::memcpy(buffer, bytes.data(), bytes.size());
		m_Commands.pop_front();
		return 0;
	}
};
}
