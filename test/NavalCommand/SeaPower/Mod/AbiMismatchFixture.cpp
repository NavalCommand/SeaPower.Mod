#include <cstdint>

// Test-only DLL: loading succeeds, but the ABI/version/export contract is invalid.
extern "C" __declspec(dllexport) std::uint32_t nc_abi_version() {
	return FIXTURE_ABI_VERSION;
}
