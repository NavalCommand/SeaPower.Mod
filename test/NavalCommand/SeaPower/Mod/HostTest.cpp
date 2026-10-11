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
	snapshot->set_schema_version(4);
	snapshot->mutable_observer()->set_value("mock-observer");
	snapshot->set_session_id(sessionId);
	snapshot->set_ready(true);
	snapshot->set_version(1);
	auto own = snapshot->add_units();
	own->mutable_identity()->set_value("own-observation");
	own->mutable_kinematics()->mutable_partial_position()->set_latitude_deg(0);
	own->mutable_kinematics()->mutable_partial_position()->set_longitude_deg(0);
	own->mutable_navigation()->mutable_planned_route();
	own->mutable_aircraft()->set_airborne(false);
	own->mutable_air_operations()->set_onboard_count(0);
	auto partial = snapshot->add_units();
	partial->mutable_identity()->set_value("ambiguous-observation");
	partial->mutable_kinematics()->mutable_partial_position()->set_latitude_deg(62);
	partial->mutable_kinematics()->mutable_partial_position()->mutable_error_ellipse()->set_major_axis_m(500);
	partial->mutable_kinematics()->mutable_partial_position()->mutable_error_ellipse()->set_minor_axis_m(250);
	auto availability = observation.add_order_availability();
	availability->set_session_id(sessionId);
	*availability->mutable_observer() = snapshot->observer();
	availability->mutable_observation()->set_value("own-observation");
	availability->set_based_on_version(1);
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
	request.mutable_observer()->set_value("mock-observer");
	request.mutable_observation()->set_value("own-observation");
	request.set_based_on_version(1);
	request.mutable_waypoint()->set_latitude_deg(62);
	request.mutable_waypoint()->set_longitude_deg(-10);
	request.set_request_id(requestId);
	return request;
}
}

TEST(Host, VersionBoundsObserverSnapshotAndSessionReset) {
	EXPECT_EQ(nc_abi_version(), 1);
	EXPECT_EQ(nc_initialize(2, "127.0.0.1:50058"), 2);
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto stub = Gateway::NewStub(grpc::CreateChannel("127.0.0.1:50058", grpc::InsecureChannelCredentials()));
	auto observation = Observation("host-one");
	ASSERT_EQ(Publish(observation), 0);
	Session connection;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->Connect(&context, Query{}, &connection).ok());
	}
	EXPECT_EQ(connection.schema_version(), 4);
	EXPECT_EQ(connection.observer().value(), "mock-observer");
	ObserverSnapshot publicSnapshot;
	{
		Query viewQuery;
		viewQuery.set_session_id(connection.session_id());
		*viewQuery.mutable_observer() = connection.observer();
		grpc::ClientContext viewContext;
		ASSERT_TRUE(stub->GetView(&viewContext, viewQuery, &publicSnapshot).ok());
	}
	ASSERT_EQ(publicSnapshot.units_size(), 2);
	EXPECT_EQ(publicSnapshot.schema_version(), 4);
	EXPECT_EQ(publicSnapshot.units(0).identity().value(), "own-observation");
	EXPECT_TRUE(publicSnapshot.units(0).aircraft().has_airborne());
	EXPECT_FALSE(publicSnapshot.units(0).aircraft().airborne());
	EXPECT_TRUE(publicSnapshot.units(0).air_operations().has_onboard_count());
	EXPECT_EQ(publicSnapshot.units(0).air_operations().onboard_count(), 0);
	EXPECT_TRUE(publicSnapshot.units(0).navigation().has_planned_route());
	EXPECT_EQ(publicSnapshot.units(0).navigation().planned_route().points_size(), 0);
	EXPECT_TRUE(publicSnapshot.units(1).kinematics().has_partial_position());
	EXPECT_TRUE(publicSnapshot.units(1).kinematics().partial_position().has_latitude_deg());
	EXPECT_FALSE(publicSnapshot.units(1).kinematics().partial_position().has_longitude_deg());
	EXPECT_TRUE(publicSnapshot.units(1).kinematics().partial_position().has_error_ellipse());
	EXPECT_EQ(publicSnapshot.units(1).kinematics().partial_position().error_ellipse().major_axis_m(), 500);
	EXPECT_EQ(publicSnapshot.GetDescriptor()->FindFieldByName("units")->message_type()->FindFieldByName("unit_id"), nullptr);

	auto request = Request("host-one", "r");
	OrderResult result;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
		EXPECT_EQ(result.observer().value(), "mock-observer");
	}
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.stage(), REQUEST_RECEIVED);
	}
	request.mutable_observation()->set_value("ambiguous-observation");
	request.set_request_id("contact-rejected");
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "order_availability_unknown");
	}
	request.mutable_observation()->set_value("own-observation");
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
	EXPECT_EQ(parsed.observation().value(), observation.snapshot().units(0).identity().value());
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
		EXPECT_EQ(result.reason(), "order_availability_unknown");
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
	ASSERT_EQ(Publish(observation), 0);
	auto request = Request("availability", "unknown");
	OrderResult result;
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "order_availability_unknown");
	}
	observation.mutable_order_availability(0)->mutable_eligibility()->add_items()->set_kind(ORDER_KIND_IDLE_WAYPOINT);
	ASSERT_EQ(Publish(observation), 0);
	request.set_request_id("unknown-row");
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
	observation = Observation("availability");
	auto launchEligibility = observation.mutable_order_availability(0)->mutable_eligibility()->add_items();
	launchEligibility->set_kind(ORDER_KIND_LAUNCH_AIRCRAFT);
	launchEligibility->set_availability(BLOCKED);
	launchEligibility->set_reason("native_launch_safety");
	auto launchOption = launchEligibility->mutable_launch_option();
	launchOption->set_count(1);
	launchOption->mutable_selection()->mutable_type()->set_value("aircraft-type");
	launchOption->mutable_selection()->set_squadron("squadron");
	launchOption->mutable_selection()->mutable_loadout()->set_value("loadout");
	ASSERT_EQ(Publish(observation), 0);
	request = Request("availability", "unsafe-launch");
	auto launch = request.mutable_launch_aircraft();
	launch->set_count(1);
	launch->mutable_selection()->mutable_type()->set_value("aircraft-type");
	launch->mutable_selection()->set_squadron("squadron");
	launch->mutable_selection()->mutable_loadout()->set_value("loadout");
	{
		grpc::ClientContext context;
		ASSERT_TRUE(stub->SubmitOrder(&context, request, &result).ok());
		EXPECT_EQ(result.reason(), "native_launch_safety");
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

TEST(Host, InvalidObservationCompositionIsRejectedWithoutCrossingAbi) {
	ASSERT_EQ(nc_initialize(1, "127.0.0.1:50058"), 0);
	auto malformed = Observation("malformed");
	*malformed.mutable_snapshot()->mutable_units(1)->mutable_identity() =
		malformed.snapshot().units(0).identity();
	EXPECT_EQ(Publish(malformed), 2);
	malformed = Observation("malformed");
	malformed.mutable_order_availability(0)->set_session_id("foreign-session");
	EXPECT_EQ(Publish(malformed), 2);
	malformed = Observation("malformed");
	malformed.mutable_snapshot()->clear_observer();
	EXPECT_EQ(Publish(malformed), 2);
	EXPECT_EQ(nc_shutdown(), 0);
}
