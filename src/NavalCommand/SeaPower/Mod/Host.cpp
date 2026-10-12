#include <NavalCommand/SeaPower/Mod/Host.h>
#include <NavalCommand/SeaPower/Mod/HostState.h>
#include <memory>

namespace
{
	constexpr uint32_t ABI_VERSION = 2;
	using Host = NavalCommand::SeaPower::Mod::HostState;

	// Explicit worker-owned shutdown only. No static destructor may join gRPC
	// threads under Windows DLL loader-lock during process termination.
	Host* host = nullptr;
	// Lifecycle is serialized by composition worker. No unload while calls run.
	template <class Callable> int Guarded(Callable&& callable) noexcept
	{
		try
		{
			return callable();
		}
		catch (...)
		{
			return 3;
		}
	}
}
extern "C" uint32_t nc_abi_version()
{
	return ABI_VERSION;
}

extern "C" int nc_initialize(uint32_t abi, const char* endpoint)
{
	return Guarded(
		[&]
		{
			if (abi != ABI_VERSION || !endpoint || host)
			{
				return 2;
			}
			auto candidate = std::make_unique<Host>();
			if (!candidate->GatewayServer.Start(endpoint))
			{
				return 3;
			}
			host = candidate.release();
			return 0;
		});
}

extern "C" int nc_publish_view(const uint8_t* buffer, uint32_t length)
{
	return Guarded(
		[&]
		{
			return host ? host->BridgeProvider.PublishView(buffer, length) : 2;
		});
}

extern "C" int nc_publish_result(const uint8_t* buffer, uint32_t length)
{
	return Guarded(
		[&]
		{
			return host ? host->BridgeProvider.PublishResult(buffer, length) : 2;
		});
}

extern "C" int nc_poll_control(uint8_t* buffer, uint32_t capacity, uint32_t* length)
{
	return Guarded(
		[&]
		{
			return host ? host->BridgeProvider.Poll(buffer, capacity, length) : 2;
		});
}

extern "C" int nc_shutdown()
{
	return Guarded(
		[]
		{
			if (host)
			{
				host->BridgeProvider.Stop();
				host->GatewayServer.Stop();
				delete host;
				host = nullptr;
			}
			return 0;
		});
}
