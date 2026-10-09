#pragma once
#include <stdint.h>
// ABI 1; UTF-8 endpoint. Caller owns every buffer for duration of call.
// Host copies on publish. Poll copies bytes; capacity failures never consume.
// 0 OK; 1 empty/busy; 2 invalid/version/size; 3 internal/start failure.
// init/shutdown run on composition worker only. DLL remains process-lifetime.
#ifdef _WIN32
#define NC_EXPORT __declspec(dllexport)
#else
#define NC_EXPORT
#endif
extern "C"
{
	NC_EXPORT uint32_t nc_abi_version();
	NC_EXPORT int nc_initialize(uint32_t abi, const char *endpoint);
	NC_EXPORT int nc_publish_view(const uint8_t *bytes, uint32_t length);
	NC_EXPORT int nc_publish_result(const uint8_t *bytes, uint32_t length);
	NC_EXPORT int nc_poll_order(uint8_t *destination, uint32_t capacity, uint32_t *length);
	NC_EXPORT int nc_shutdown();
}
