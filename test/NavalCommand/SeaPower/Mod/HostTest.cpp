#include <NavalCommand/SeaPower/Mod/Bridge.h>
#include <NavalCommand/SeaPower/Mod/Host.h>
#include <future>
#include <gtest/gtest.h>
#include <thread>

namespace NavalCommand::SeaPower::Mod
{
	using namespace std::chrono_literals;

	void Publish(Bridge& bridge)
	{
		Wire::Publication source;
		source.set_session("mission");
		source.set_revision(1);
		source.add_views()->mutable_scope()->mutable_omniscient();
		const auto bytes = source.SerializeAsString();
		ASSERT_EQ(bridge.PublishView(reinterpret_cast<const std::uint8_t*>(bytes.data()), static_cast<std::uint32_t>(bytes.size())), 0);
	}

	TEST(Bridge, HoldsOwnedPublicationAndRejectsWrongScope)
	{
		Bridge bridge;
		Publish(bridge);
		auto retained = bridge.Publication();
		Wire::Publication source;
		source.set_session("new");
		auto view = source.add_views();
		view->mutable_scope()->set_faction("blue");
		view->add_entries()->mutable_result()->mutable_unit()->set_faction("red");
		const auto bytes = source.SerializeAsString();
		EXPECT_EQ(bridge.PublishView(reinterpret_cast<const std::uint8_t*>(bytes.data()), static_cast<std::uint32_t>(bytes.size())), 2);
		EXPECT_EQ(bridge.Publication(), retained);
		EXPECT_EQ(retained->session(), "mission");
	}

	TEST(Bridge, WaitsForEngineResultAndPreservesBufferCapacity)
	{
		Bridge bridge;
		Publish(bridge);
		Wire::Control request;
		request.set_session("mission");
		request.set_client("owner");
		request.set_pause(true);
		auto future = std::async(std::launch::async,
								 [&]
								 {
									 return bridge.Apply(request, std::chrono::steady_clock::now() + 2s);
								 });
		std::uint8_t buffer[4096];
		std::uint32_t length{};
		int polled = 1;
		for (int attempt = 0; attempt < 100 && polled == 1; ++attempt)
		{
			polled = bridge.Poll(buffer, 1, &length);
			std::this_thread::sleep_for(1ms);
		}
		ASSERT_EQ(polled, 2);
		ASSERT_GT(length, 1u);
		ASSERT_EQ(bridge.Poll(buffer, sizeof(buffer), &length), 0);
		Wire::Control delivered;
		ASSERT_TRUE(delivered.ParseFromArray(buffer, length));
		EXPECT_EQ(delivered.client(), "owner");
		EXPECT_TRUE(delivered.pause());
		EXPECT_GT(delivered.expires_unix_milliseconds(), 0);
		EXPECT_EQ(future.wait_for(10ms), std::future_status::timeout);
		Wire::ControlResult result;
		result.set_request(delivered.request());
		result.mutable_result()->mutable_success();
		const auto bytes = result.SerializeAsString();
		ASSERT_EQ(bridge.PublishResult(reinterpret_cast<const std::uint8_t*>(bytes.data()), static_cast<std::uint32_t>(bytes.size())), 0);
		ASSERT_TRUE(future.get());
		bridge.Disconnected("owner");
		bridge.Disconnected("owner");
		ASSERT_EQ(bridge.Poll(buffer, sizeof(buffer), &length), 0);
		Wire::Control release;
		ASSERT_TRUE(release.ParseFromArray(buffer, length));
		EXPECT_TRUE(release.has_disconnected());
		EXPECT_EQ(release.client(), "owner");
		EXPECT_EQ(bridge.Poll(buffer, sizeof(buffer), &length), 1);
	}

	TEST(Bridge, ExpiredCommandsCannotBeDeliveredAndDisconnectHasPriority)
	{
		Bridge bridge;
		Publish(bridge);
		Wire::Control request;
		request.set_session("mission");
		request.set_client("owner");
		request.set_pause(true);
		auto outcome = bridge.Apply(request, std::chrono::steady_clock::now() + 10ms);
		ASSERT_FALSE(outcome);
		std::uint8_t buffer[4096];
		std::uint32_t length{};
		bridge.Disconnected("owner");
		EXPECT_EQ(bridge.Poll(buffer, sizeof(buffer), &length), 1);
	}

	TEST(Host, RejectsAbiMismatchAndSupportsOrderlyLifecycle)
	{
		EXPECT_EQ(nc_abi_version(), 2u);
		EXPECT_EQ(nc_initialize(1, "127.0.0.1:0"), 2);
		ASSERT_EQ(nc_initialize(2, "127.0.0.1:0"), 0);
		EXPECT_EQ(nc_initialize(2, "127.0.0.1:0"), 2);
		EXPECT_EQ(nc_publish_view(nullptr, 1), 2);
		EXPECT_EQ(nc_publish_view(nullptr, 0), 0);
		EXPECT_EQ(nc_shutdown(), 0);
		EXPECT_EQ(nc_shutdown(), 0);
	}
}
