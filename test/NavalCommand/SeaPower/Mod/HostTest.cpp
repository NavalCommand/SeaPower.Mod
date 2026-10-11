#include <NavalCommand/Gateway/Api/Codec.h>
#include <NavalCommand/SeaPower/Mod/Host.h>
#include <grpcpp/grpcpp.h>
#include <gtest/gtest.h>
#include <navalcommand/gateway/v1/service.grpc.pb.h>
using namespace navalcommand::gateway::v1;

namespace {
ProviderObservation Observation(const std::string &sessionId) {
	ProviderObservation observation;
	auto snapshot = observation.mutable_snapshot();
	snapshot->mutable_session()->set_schema_version(3);
	snapshot->mutable_session()->set_session_id(sessionId);
	snapshot->mutable_session()->set_ready(true);
	snapshot->set_observation_version(1);
	snapshot->add_instances()->mutable_unit_id()->set_value("own");
	snapshot->add_instances()->mutable_unit_id()->set_value("contact");
	auto availability = observation.add_order_availability();
	availability->set_session_id(sessionId);
	availability->mutable_unit_id()->set_value("own");
	availability->set_based_on_version(1);
	availability->mutable_eligibility()->set_coverage(COVERAGE_COMPLETE);
	auto eligibility = availability->mutable_eligibility()->add_items();
	eligibility->set_kind(ORDER_KIND_IDLE_WAYPOINT);
	eligibility->set_availability(EXECUTABLE);
	return observation;
}
int Publish(const ProviderObservation &observation) {
	const auto bytes = observation.SerializeAsString();
	return nc_publish_view(reinterpret_cast<const uint8_t *>(bytes.data()), static_cast<uint32_t>(bytes.size()));
}
OrderRequest Request(const std::string &sessionId, const std::string &requestId) {
	OrderRequest request;
	request.set_session_id(sessionId);
	request.mutable_unit_id()->set_value("own");
	request.set_based_on_version(1);
	request.mutable_waypoint()->set_latitude_deg(62);
	request.mutable_waypoint()->set_longitude_deg(-10);
	request.set_request_id(requestId);
	return request;
}
}

TEST(Host, VersionBoundsPortAndSessionReset) {
	EXPECT_EQ(nc_abi_version(), 1);
	EXPECT_EQ(nc_initialize(2, "127.0.0.1:50058"), 2);
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto stub = Gateway::NewStub(grpc::CreateChannel("127.0.0.1:50058", grpc::InsecureChannelCredentials()));
	auto observation = Observation("host-one");
	ASSERT_EQ(Publish(observation), 0);
	auto request = Request("host-one", "r");
	OrderResult result;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
	}
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
	}
	request.mutable_unit_id()->set_value("contact");
	request.set_request_id("contact-rejected");
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "wrong_side_or_invalid_entity");
	}
	request.mutable_unit_id()->set_value("own");
	constexpr std::size_t MAXIMUM_ORDER_BYTES = 65'536;
	uint8_t undersizedBuffer[1];
	uint8_t buffer[MAXIMUM_ORDER_BYTES];
	uint32_t length = 0;
	EXPECT_EQ(nc_poll_order(undersizedBuffer, 1, &length), 2);
	EXPECT_GT(length, 1);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 0);
	OrderRequest parsed;
	ASSERT_TRUE(parsed.ParseFromArray(buffer, length));
	EXPECT_EQ(parsed.request_id(), "r");
	EXPECT_EQ(parsed.unit_id().value(), observation.snapshot().instances(0).unit_id().value());
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	for (int index = 0; index < 65; ++index) {
		request.set_request_id("queue-" + std::to_string(index));
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		if (index == 64) {
			EXPECT_EQ(result.reason(), "queue_full");
		}
	}
	observation = Observation("host-two");
	ASSERT_EQ(Publish(observation), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	{
		grpc::ClientContext context;
		request.set_request_id("old");
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "stale_session");
	}
	// Reused observation ID without current control availability remains unauthorized.
	observation.clear_order_availability();
	ASSERT_EQ(Publish(observation), 0);
	request.set_session_id("host-two");
	request.set_request_id("reused-without-authority");
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "wrong_side_or_invalid_entity");
	}
	EXPECT_EQ(nc_publish_view(buffer, 1'048'577), 2);
	EXPECT_EQ(nc_shutdown(), 0);
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 2);
	EXPECT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	EXPECT_EQ(nc_shutdown(), 0);
}

TEST(Host, UnknownBlockedAndInvalidPayloadDoNotEnterQueue) {
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto stub = Gateway::NewStub(grpc::CreateChannel("127.0.0.1:50058", grpc::InsecureChannelCredentials()));
	auto observation = Observation("availability");
	observation.mutable_order_availability(0)->mutable_eligibility()->clear_items();
	observation.mutable_order_availability(0)->mutable_eligibility()->set_coverage(COVERAGE_UNKNOWN);
	ASSERT_EQ(Publish(observation), 0);
	auto request = Request("availability", "unknown");
	OrderResult result;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "order_availability_unknown");
	}
	observation = Observation("availability");
	auto eligibility = observation.mutable_order_availability(0)->mutable_eligibility()->mutable_items(0);
	eligibility->set_availability(BLOCKED);
	eligibility->set_reason("protected_native_action");
	ASSERT_EQ(Publish(observation), 0);
	request.set_request_id("blocked");
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "protected_native_action");
	}
	request.clear_payload();
	{
		grpc::ClientContext context;
		EXPECT_EQ(stub->SubmitOrder(&context, request, &result).error_code(), grpc::StatusCode::INVALID_ARGUMENT);
	}
	uint8_t buffer[65'536];
	uint32_t length = 0;
	EXPECT_EQ(nc_poll_order(buffer, sizeof(buffer), &length), 1);
	EXPECT_EQ(nc_shutdown(), 0);
}

TEST(Host, InvalidCompositionIsRejectedWithoutCrossingAbi) {
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto malformed = Observation("malformed");
	malformed.mutable_snapshot()->mutable_instances(1)->mutable_equipment()->add_items();
	EXPECT_EQ(Publish(malformed), 2);
	malformed.mutable_snapshot()->mutable_instances(1)->mutable_equipment()->clear_items();
	malformed.mutable_snapshot()->mutable_session()->set_schema_version(2);
	EXPECT_EQ(Publish(malformed), 2);
	malformed = Observation("malformed");
	malformed.mutable_order_availability(0)->set_session_id("foreign-session");
	EXPECT_EQ(Publish(malformed), 2);
	EXPECT_EQ(nc_shutdown(), 0);
}
