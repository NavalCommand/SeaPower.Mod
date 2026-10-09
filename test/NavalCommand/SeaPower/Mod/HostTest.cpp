#include "Host.h"
#include <NavalCommand/Gateway/Api/Codec.h>
#include <grpcpp/grpcpp.h>
#include <gtest/gtest.h>
#include <navalcommand/gateway/v1/service.grpc.pb.h>
using namespace navalcommand::gateway::v1;
TEST(Host, VersionBoundsPortAndSessionReset) {
	EXPECT_EQ(nc_abi_version(), 1);
	EXPECT_EQ(nc_initialize(2, "127.0.0.1:50058"), 2);
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto stub = Gateway::NewStub(grpc::CreateChannel("127.0.0.1:50058", grpc::InsecureChannelCredentials()));
	WorldSnapshot view;
	view.mutable_session()->set_schema_version(1);
	view.mutable_session()->set_session_id("host-one");
	view.mutable_session()->set_ready(true);
	view.set_observation_version(1);
	view.add_own_units()->set_id("own");
	auto bytes = view.SerializeAsString();
	ASSERT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 0);
	OrderRequest request;
	request.set_session_id("host-one");
	request.set_entity_id("own");
	request.set_based_on_version(1);
	request.mutable_waypoint()->set_latitude_deg(62);
	request.mutable_waypoint()->set_longitude_deg(-10);
	request.set_request_id("r");
	OrderResult result;
	{
		grpc::ClientContext c;
		ASSERT_TRUE(stub->SubmitOrder(&c, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
	}
	{
		grpc::ClientContext c;
		ASSERT_TRUE(stub->SubmitOrder(&c, request, &result).ok());
	}
	uint8_t small[1], buffer[65536];
	uint32_t length = 0;
	EXPECT_EQ(nc_poll_order(small, 1, &length), 2);
	EXPECT_GT(length, 1);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 0);
	OrderRequest parsed;
	ASSERT_TRUE(parsed.ParseFromArray(buffer, length));
	EXPECT_EQ(parsed.request_id(), "r");
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	for (int i = 0; i < 65; ++i) {
		request.set_request_id("queue-" + std::to_string(i));
		grpc::ClientContext c;
		ASSERT_TRUE(stub->SubmitOrder(&c, request, &result).ok());
		if (i == 64) {
			EXPECT_EQ(result.reason(), "queue_full");
		}
	}
	view.mutable_session()->set_session_id("host-two");
	bytes = view.SerializeAsString();
	EXPECT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	{
		grpc::ClientContext c;
		request.set_request_id("old");
		ASSERT_TRUE(stub->SubmitOrder(&c, request, &result).ok());
		EXPECT_EQ(result.reason(), "stale_session");
	}
	EXPECT_EQ(nc_publish_view(buffer, 1048577), 2);
	EXPECT_EQ(nc_shutdown(), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 2);
	EXPECT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	EXPECT_EQ(nc_shutdown(), 0);
}
