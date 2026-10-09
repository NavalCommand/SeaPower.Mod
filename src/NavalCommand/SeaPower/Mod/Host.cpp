#include "Host.h"
#include <NavalCommand/Gateway/Api/Codec.h>
#include <NavalCommand/Gateway/Grpc/Server.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <deque>
#include <map>
#include <mutex>
namespace {
using namespace NavalCommand;
class Bridge final : public Gateway::Api::IProvider {
	std::mutex mutex_;
	World::Api::View view_;
	std::deque<std::string> commands_;
	std::map<std::string, Orders::Api::Result> ledger_;

  public:
	std::expected<World::Api::View, Gateway::Api::Error> View() override {
		std::lock_guard l(mutex_);
		if (!view_.ready) {
			return std::unexpected(Gateway::Api::Error::NotReady);
		}
		return view_;
	}
	Orders::Api::Result Submit(const Orders::Api::Request &r) override {
		std::lock_guard l(mutex_);
		using S = Orders::Api::Stage;
		auto reject = [&](const char *why) {
			return Orders::Api::Result{r.sessionId, r.requestId, S::Rejected, why};
		};
		if (!view_.ready) {
			return reject("provider_not_ready");
		}
		if (r.sessionId != view_.sessionId) {
			return reject("stale_session");
		}
		if (auto i = ledger_.find(r.requestId); i != ledger_.end()) {
			return i->second;
		}
		if (r.requestId.empty() || r.requestId.size() > 128 || r.entityId.size() > 128) {
			return reject("invalid_arguments");
		}
		if (ledger_.size() >= 128) {
			return reject("ledger_full");
		}
		auto result = Orders::Api::Result{r.sessionId, r.requestId, S::Received, "mailbox_received"};
		if (!std::ranges::any_of(view_.units, [&](auto &u) {
				return u.id == r.entityId;
			})) {
			result = reject("wrong_side_or_invalid_entity");
		} else if (!r.waypoint || !std::isfinite(r.waypoint->latitudeDeg) || !std::isfinite(r.waypoint->longitudeDeg) || std::abs(r.waypoint->latitudeDeg) > 90 ||
				   std::abs(r.waypoint->longitudeDeg) > 180) {
			result = reject("invalid_arguments");
		} else if (r.basedOnVersion == 0 || r.basedOnVersion > view_.version || view_.version - r.basedOnVersion > 20) {
			result = reject("stale_observation");
		} else if (commands_.size() >= 64) {
			result = reject("queue_full");
		} else {
			commands_.push_back(Gateway::Api::Encode(r).SerializeAsString());
		}
		ledger_[r.requestId] = result;
		return result;
	}
	Orders::Api::Result Status(const std::string &s, const std::string &id) override {
		std::lock_guard l(mutex_);
		if (s != view_.sessionId) {
			return {s, id, Orders::Api::Stage::Rejected, "stale_session"};
		}
		if (auto i = ledger_.find(id); i != ledger_.end()) {
			return i->second;
		}
		return {s, id, Orders::Api::Stage::Unknown, "unknown_or_expired_request"};
	}
	int PublishView(const uint8_t *data, uint32_t size) {
		Gateway::Api::Wire::WorldSnapshot w;
		if (!data || size > 1048576 || !w.ParseFromArray(data, size) || w.session().schema_version() != 1) {
			return 2;
		}
		auto d = Gateway::Api::Decode(w);
		std::unique_lock l(mutex_, std::try_to_lock);
		if (!l) {
			return 1;
		}
		if (d.sessionId != view_.sessionId || !d.ready) {
			commands_.clear();
			ledger_.clear();
		}
		view_ = std::move(d);
		return 0;
	}
	int PublishResult(const uint8_t *data, uint32_t size) {
		Gateway::Api::Wire::OrderResult w;
		if (!data || size > 65536 || !w.ParseFromArray(data, size)) {
			return 2;
		}
		auto r = Gateway::Api::Decode(w);
		std::unique_lock l(mutex_, std::try_to_lock);
		if (!l) {
			return 1;
		}
		if (r.sessionId != view_.sessionId) {
			return 2;
		}
		if (!ledger_.contains(r.requestId)) {
			return 2;
		}
		ledger_[r.requestId] = std::move(r);
		return 0;
	}
	int Poll(uint8_t *data, uint32_t capacity, uint32_t *length) {
		if (!length || !data || capacity > 65536) {
			return 2;
		}
		*length = 0;
		std::unique_lock l(mutex_, std::try_to_lock);
		if (!l || commands_.empty()) {
			return 1;
		}
		auto &bytes = commands_.front();
		*length = static_cast<uint32_t>(bytes.size());
		if (capacity < bytes.size()) {
			return 2;
		}
		std::memcpy(data, bytes.data(), bytes.size());
		commands_.pop_front();
		return 0;
	}
};
struct Host {
	Bridge bridge;
	Gateway::Grpc::Server server{bridge, {}};
};
// Explicit worker-owned shutdown only. No static destructor may join gRPC
// threads under Windows DLL loader-lock during process termination.
Host *host = nullptr;
// Lifecycle is serialized by composition worker. No unload while calls run.
template <class F> int guarded(F &&f) noexcept {
	try {
		return f();
	} catch (...) {
		return 3;
	}
}
} // namespace
extern "C" uint32_t nc_abi_version() {
	return 1;
}
extern "C" int nc_initialize(uint32_t abi, const char *endpoint) {
	return guarded([&] {
		if (abi != 1 || !endpoint || host) {
			return 2;
		}
		auto candidate = std::make_unique<Host>();
		if (!candidate->server.Start(endpoint)) {
			return 3;
		}
		host = candidate.release();
		return 0;
	});
}
extern "C" int nc_publish_view(const uint8_t *b, uint32_t n) {
	return guarded([&] {
		return host ? host->bridge.PublishView(b, n) : 2;
	});
}
extern "C" int nc_publish_result(const uint8_t *b, uint32_t n) {
	return guarded([&] {
		return host ? host->bridge.PublishResult(b, n) : 2;
	});
}
extern "C" int nc_poll_order(uint8_t *b, uint32_t n, uint32_t *l) {
	return guarded([&] {
		return host ? host->bridge.Poll(b, n, l) : 2;
	});
}
extern "C" int nc_shutdown() {
	return guarded([] {
		if (host) {
			host->server.Stop();
			delete host;
			host = nullptr;
		}
		return 0;
	});
}
