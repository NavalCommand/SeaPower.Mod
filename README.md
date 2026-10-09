# SeaPower.Mod 0.1.0
Sole embedded composition root. C# bootstrap/DI, immutable queues, native C ABI shim, configuration, source fetch/build/package, approved test harness.

./scripts/Build.ps1 -Protoc ABSOLUTE_SOURCE_BUILT_PROTOC fetches pinned managed modules; CPM fetches pinned C++ sources; builds/tests the native host, builds managed provider/root and the reused Task002 harness, and assembles out/package/0.1.0. Set VCPKG_ROOT, NC_VCPKG_INSTALLED_DIR and NC_CMAKE first. Each dependency remains an independently buildable Git repository. No direct provider-gateway dependency. Module locks may use local Git URLs; override NC_REPOSITORY_BASE to relocate sources.

C ABI revision 1 belongs only to native/Host.h/.cpp. Poll/publish copy canonical Gateway.Api bytes; max snapshot 1 MiB, command 64 KiB, 64 queued orders, 128 retained IDs. Caller owns buffers. No exceptions cross C linkage. No DllMain hooks. Start/Stop occur on managed I/O worker, never simulation callback. A stopped native module is kept loaded until process exit; forcibly unloading a DLL with gRPC code is not supported.

Package excludes proprietary game references. Runtime and loader compatibility is installation-specific. Endpoint comes from config.json through test launcher, loopback only. Normal launches are opt-out; no client auto-launch.

scripts/Start-Live.ps1 -OutputDirectory NEW_DIR -Cli ABSOLUTE_CLI_PATH [-Smoke] loads the staged package through the approved harness, temporarily replaces ONLY our existing LaunchCandidate.dll, enables the existing loader for startup, runs a separate read-only CLI (or explicit smoke), closes the owned game process and restores our DLL/config. No new preloader or unmanaged loader installation. All native DLL/package files stay outside the game installation. The harness sources are copies of our existing Tasks002–003 helper, plus one opt-in composition bootstrap call.

Native crash can terminate the game. Mission unload invalidates session/queues; server may remain process-lifetime and report provider unavailable. Worker shutdown never joins from Unity callbacks. Clean process teardown/unload coverage must be reported from actual evidence, not assumed.

scripts/Test-Mock.ps1 runs the independently built generic gateway mock host and the same CLI without this mod or Sea Power.

