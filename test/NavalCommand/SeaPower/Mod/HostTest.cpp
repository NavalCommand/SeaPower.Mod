#include <NavalCommand/Gateway/Api/Codec.h>
#include <NavalCommand/SeaPower/Mod/Host.h>
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
	view.mutable_session()->set_schema_version(2);
	view.mutable_session()->set_session_id("host-one");
	view.mutable_session()->set_ready(true);
	view.set_observation_version(1);
	view.add_instances()->mutable_subject()->set_own_unit_id("own");
	auto contact = view.add_instances();
	contact->mutable_subject()->set_track_id("contact");
	auto bytes = view.SerializeAsString();
	ASSERT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 0);
	OrderRequest request;
	request.set_session_id("host-one");
	request.mutable_subject()->set_own_unit_id("own");
	request.set_based_on_version(1);
	request.mutable_waypoint()->set_latitude_deg(62);
	request.mutable_waypoint()->set_longitude_deg(-10);
	request.set_request_id("r");
	OrderResult result;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
	}
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
	}
	request.mutable_subject()->set_own_unit_id("contact");
	request.set_request_id("contact-rejected");
	{
		grpc::ClientContext trackContext;
		ASSERT_TRUE(stub->SubmitOrder(&trackContext, request, &result).ok());
		EXPECT_EQ(result.reason(), "wrong_side_or_invalid_entity");
	}
	request.mutable_subject()->set_own_unit_id("own");
	constexpr std::size_t MAXIMUM_ORDER_BYTES = 65'536;
	uint8_t undersizedBuffer[1];
	uint8_t buffer[MAXIMUM_ORDER_BYTES];
	uint32_t length = 0;
	EXPECT_EQ(nc_poll_order(undersizedBuffer, 1, &length), 2);
	EXPECT_GT(length, 1);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 0);
	OrderRequest parsedRequest;
	ASSERT_TRUE(parsedRequest.ParseFromArray(buffer, length));
	EXPECT_EQ(parsedRequest.request_id(), "r");
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	for (int commandIndex = 0; commandIndex < 65; ++commandIndex) {
		request.set_request_id("queue-" + std::to_string(commandIndex));
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		if (commandIndex == 64) {
			EXPECT_EQ(result.reason(), "queue_full");
		}
	}
	view.mutable_session()->set_session_id("host-two");
	bytes = view.SerializeAsString();
	EXPECT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	{
		grpc::ClientContext context;
		request.set_request_id("old");
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "stale_session");
	}
	EXPECT_EQ(nc_publish_view(buffer, 1048577), 2);
	EXPECT_EQ(nc_shutdown(), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 2);
	EXPECT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	EXPECT_EQ(nc_shutdown(), 0);
}

TEST(Host, InvalidCompositionIsRejectedWithoutCrossingAbi) {
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	WorldSnapshot malformed;
	malformed.mutable_session()->set_schema_version(2);
	malformed.add_instances()->mutable_subject()->set_track_id("contact");
	malformed.mutable_instances(0)->mutable_equipment()->add_items();
	auto bytes = malformed.SerializeAsString();
	EXPECT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 2);
	malformed.mutable_instances(0)->mutable_equipment()->clear_items();
	malformed.mutable_session()->set_schema_version(1);
	bytes = malformed.SerializeAsString();
	EXPECT_EQ(nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), bytes.size()), 2);
	EXPECT_EQ(nc_shutdown(), 0);
}
