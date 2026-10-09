# SeaPower.Mod 0.1.0

Sole composition root; runtime code, C ABI revision 1, provider/gateway separation, loopback configuration, bounded immutable queues and approved test harness remain unchanged from Task 004. Native host production target is defined in src/; private namespace-mirrored Host.cpp/.h live there. ABI tests live in test/ and append the host output directory only to their test loader search path.

scripts/Build.ps1 -GameRoot ACTUAL_INSTALL -CMake cmake [-BuildCMakeSource ACTUAL_CHECKOUT] composes/builds/tests the native modules, consumes CMake-generated managed-paths.json for the canonical API/provider sources, compiles managed root/harness and packages out/package/0.1.0. There is no second managed Git resolver or hardcoded module URL substitution. Set game references in ignored Build.Local.cmake for independent adapter builds. No proprietary files or platform Span DLLs are packaged; existing Unity platform shims are used read-only.

scripts/Start-Live.ps1 -GameRoot ACTUAL_INSTALL -OutputDirectory NEW_DIR -Cli ACTUAL_CLI [-Smoke|-Blocked|-ReloadTest] preserves the existing opt-in test behavior and exact helper/loader rollback. It closes only its owned game process. scripts/Test-Mock.ps1 runs the independent generic host and CLI. Package remains outside the game installation. Normal launch stays inactive and no client is auto-started by the mod.

Native crashes can terminate the game. Mission unload clears handles/queues and the process-lifetime server accepts a new session. Never unload a running gRPC DLL. No worker join in Unity callbacks; graceful game exit remains the Task 004 validation gap. No new AirOps, gateway or gameplay capability is introduced.

## Build structure (Task 005)

Root CMakeLists.txt initializes/composes only. Shared policy/resolution lives in the independent Build.CMake repository, fetched as ncbuild. Production libraries are defined in src/, mocks in mock/, tests in test/, applications in app/. Namespace paths and existing target names are preserved.

Enter a VS 2026 x64 developer shell, set standard VCPKG_ROOT, and use CMake 4.2+ and Ninja:

```powershell
cmake --preset windows-msvc-release -DFETCHCONTENT_SOURCE_DIR_NCBUILD=/absolute/path/to/Build.CMake
cmake --build o/windows-msvc-release --parallel 8
ctest --test-dir o/windows-msvc-release --output-on-failure --no-tests=error
```

The bootstrap option is unnecessary once Build.CMake is available at its declared Git URL. Optional ignored Build.Local.cmake registers canonical module URLs with NavalCommandSetDependencyPath(URL, absoluteCheckout); it is read once at the top level before declaration and applies transitively. Without a registration the declared Git URL and main reference are used. There is no workspace-root environment variable or implicit sibling lookup. External dependencies remain source-built vcpkg manifest packages. An already provisioned prefix can use standard -DVCPKG_INSTALLED_DIR=... -DVCPKG_MANIFEST_INSTALL=OFF cache options. See the parent project's Docs/MODULE_STRUCTURE.md for exact verified commands and limits.

Presets contain configurePresets only, windows-msvc-debug/release and linux-clang-debug/release where applicable. Output is o/${presetName}. C++26 policy is centralized; MSVC uses /std:c++latest for available features. No full C++26 conformance claim. Build outputs, Build.Local.cmake and CMakeUserPresets.json are ignored. No proprietary game binaries are included.
