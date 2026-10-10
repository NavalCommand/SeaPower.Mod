# SeaPower.Mod 0.1.0

Project-wide documentation is maintained in the separate **Docs Git repository**. Before changing this module, explicitly read [Docs/AGENTS.md](../Docs/AGENTS.md), [architecture](../Docs/ARCHITECTURE.md), [module/build policy](../Docs/MODULE_STRUCTURE.md), [local sources](../Docs/LOCAL_SOURCES.md), and [code style](../Docs/CODE_STYLE.md) with its [owner export](../Docs/CodeStyle.xml). The shared instructions are not discovered automatically across repository boundaries; do not create a competing AGENTS.md in the parent workspace. Historical reports and reproduction guides are indexed under [Docs/Tasks/](../Docs/Tasks/README.md), with run receipts under [Docs/Evidence/](../Docs/Evidence/README.md).




The composition root preserves C ABI revision 1, provider/gateway separation, loopback-only hosting, bounded byte mailboxes and protected native command dispatch. Native host production targets remain in `src/`; ABI and managed lifecycle/configuration tests live in `test/`.

## Production runtime

`managed/runtime/SeaPower.Mod.csproj` builds `SeaPower.Mod.dll`. Its BepInEx plugin ID is `dev.navalcommand.seapower.mod`. The plugin independently reads configuration and loads metadata on a worker before composing the provider on the verified game thread. It does not depend on a harness, scenario launcher, test receipt, or external client.

The runtime reads `config.json` alongside its assembly; `NC_MOD_CONFIG` may select an absolute alternate file. Configuration schema/ABI 1, loopback endpoint, fixed existing message/queue bounds and disabled automatic client launch are validated. `enabledByDefault` remains false. Explicitly activate with `NC_MOD_ENABLED=1` (or an enabled configuration); `0` explicitly disables it. Other values are errors. `nativeLibrary` defaults to `SeaPower.Host.dll` alongside the assembly. `logDirectory` defaults to the user's local application-data `NavalCommand/SeaPower.Mod` directory; `NC_MOD_LOG_DIRECTORY` can override it. Relative JSON paths resolve against the module directory. Configuration preparation and normal log I/O run off-thread. Startup errors appear in the BepInEx log, even when the runtime log directory is unwritable.

The selected observer comes from `SceneCreator.PlayerSideName` when the current player taskforce is ready; the runtime never forces a side. `SeaPower.Client` continues filtering against the actual native player taskforce. Scene unload invalidates the session, handles and pending commands; reloading binds the newly selected player side. Teardown removes scheduling patches/events and signals the worker, without joining it in an engine callback. The native DLL remains loaded for process lifetime.

For an approved deployment, place the runtime package files together under `BepInEx/plugins/NavalCommand.Runtime/`, using an already approved compatible loader installation. No loader or proprietary game assembly is redistributed. Without explicit activation the plugin remains inactive.

## Separate development harness

`managed/harness/SeaPower.Harness.csproj` builds `SeaPower.Harness.dll`, plugin ID `dev.navalcommand.seapower.harness`. It owns only scenario launch, narrow playable readiness, bounded evidence and explicitly requested mission reload. It cannot load or enable the production runtime, issue operational commands, run arbitrary code or install historical probes.

The harness is inactive unless `NC_HARNESS_ENABLED=1`. A controlled run also requires absolute `NC_HARNESS_SCENARIO` and `NC_HARNESS_RUN_DIRECTORY`, the mission's native `NC_HARNESS_SIDE`, and a GUID `NC_HARNESS_TOKEN`. `NC_HARNESS_RELOAD=1` requests the maintained reload regression action. The launcher validates mission side and the inspected game assembly hash before starting. Receipts retain `ready.json` with token/PID identity; diagnostics are now in `harness.log`. Evidence failures cannot count as readiness. Former probe/interference switches are removed, with historical sources archived in the authoritative Docs task archive.

After CMake configuration, `cmake --build o/windows-msvc-release --target package-runtime` builds the native host, then the runtime's transitive MSBuild graph and stages `o/package/0.1.0`. The fresh production allowlist remains four permitted managed DLLs, native host, disabled configuration, licenses and manifest. No harness/scenarios/probes/game references/platform `System.*` DLLs are redistributed.

`cmake --build o/windows-msvc-release --target package-harness` builds and stages only the harness and reusable scenarios in `o/harness/0.1.0`. No runtime/provider/native host compilation is needed for this target. Existing stages move to recoverable backups. The Build/Build-Harness scripts are thin wrappers over these targets, using already configured source/tool contexts.

`scripts/Start-Live.ps1 -GameRoot ACTUAL_INSTALL -OutputDirectory NEW_DIR -ApproveLoaderChanges` requires explicit authorization for temporary loader/plugin changes and always removes only its owned deployments/process. Choose `-Mode Harness` for scenario readiness without the mod, `-Mode Production` for the mod without any scenario launcher (select a mission through the game menu), or `-Mode Combined` for both independently enabled plugins. `-RuntimeDisabled` exercises the disabled posture. Existing game sessions and deployments are refused. Loader bytes are restored exactly on success or failure.

Add `-Cli ACTUAL_CLI` for external validation; `-Smoke`, `-Blocked` and `-ReloadTest` explicitly select existing regression checks. `-NormalExit` requests normal window-close and records failure separately from forced cleanup. The ordinary cleanup is an owned-process forced termination, not proof of graceful shutdown. `scripts/Start-Validation.ps1` is a harness-only readiness wrapper; it now always closes its owned run. `scripts/Test-Mock.ps1` preserves independent mock-host/client checks. Packages and evidence stay outside the game installation.

Native crashes can terminate the game. Never unload a running gRPC DLL. A live production-only Release test has verified normal window-close with `worker-stop status=0`; this is bounded evidence for that build/loader, not a general shutdown guarantee. The previously recorded Debug gRPC process-exit failure remains an unresolved issue; no suppression or gateway rewrite is included. Verification receipts and current outcomes live in the Docs evidence archive.

## Build structure

Root CMakeLists.txt initializes/composes only. Shared policy/resolution lives in the independent Build.CMake repository, fetched as ncbuild. Production libraries are defined in src/, mocks in mock/, tests in test/, applications in app/. Namespace paths and existing target names are preserved.

Enter a VS 2026 x64 developer shell, set standard VCPKG_ROOT, and use CMake 4.2+ and Ninja:

```powershell
cmake --preset windows-msvc-release -DFETCHCONTENT_SOURCE_DIR_NCBUILD=/absolute/path/to/Build.CMake
cmake --build o/windows-msvc-release --parallel 8
ctest --test-dir o/windows-msvc-release --output-on-failure --no-tests=error
```

The bootstrap option is unnecessary once Build.CMake is available at its declared Git URL. Optional ignored Build.Local.cmake registers canonical module URLs with NavalCommandSetDependencyPath(URL, absoluteCheckout); it is read once at the top level before declaration and applies transitively. Without a registration the declared Git URL and main reference are used. There is no workspace-root environment variable or implicit sibling lookup. External dependencies remain source-built vcpkg manifest packages. An already provisioned prefix can use standard -DVCPKG_INSTALLED_DIR=... -DVCPKG_MANIFEST_INSTALL=OFF cache options. See the separate [Docs repository build policy](../Docs/MODULE_STRUCTURE.md) for current commands and limits; dated measurements belong to the [task archive](../Docs/Tasks/README.md).

Presets contain configurePresets only, windows-msvc-debug/release and linux-clang-debug/release where applicable. Output is o/${presetName}. C++26 policy is centralized; MSVC uses /std:c++latest for available features. No full C++26 conformance claim. Build outputs, Build.Local.cmake and CMakeUserPresets.json are ignored. No proprietary game binaries are included.

Windows target and host dependencies use `x64-windows-static`, matching `/MT` in Release and `/MTd` in Debug. Reconfigure with `--fresh` and relink when migrating earlier dynamic-CRT artifacts.

## Managed build integration

Follow the authoritative [managed build policy](../Docs/MANAGED_BUILD.md). One CMake configure resolves internal source dependencies and persists each managed repository's ignored `o/managed/Build.Context.props`. Then ordinary `.csproj` builds in Visual Studio/Rider or `dotnet build ... -c Debug/Release` use those sources through real ProjectReferences without extra properties or another configure. Change source/tool/game-reference locations by reconfiguring, not by editing generated projects. Conflicting contexts require explicit `-DNAVALCOMMAND_REPLACE_MANAGED_CONTEXT=ON`; existing CMake consumers must then be reconfigured.

All repository-owned generated files stay under `o/`: native preset directories, per-project MSBuild `o/managed/<project>/{bin,obj}/` directories and mod `o/package`/`o/harness` stages. MSBuild preserves configuration/platform/framework dimensions. The shared helper discovers managed/CMakeLists.txt and invokes only the requested top-level project; MSBuild owns NuGet and transitive compilation. Game-reference absence blocks only requested game-dependent projects.
