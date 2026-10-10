#pragma once
#include <NavalCommand/Gateway/Grpc/Server.h>
#include <NavalCommand/SeaPower/Mod/Bridge.h>

namespace NavalCommand::SeaPower::Mod
{
	struct HostState
	{
		Bridge BridgeProvider;
		Gateway::Grpc::Server GatewayServer{BridgeProvider, {}};
	};
} // namespace NavalCommand::SeaPower::Mod
