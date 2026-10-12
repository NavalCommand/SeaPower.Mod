# SeaPower.Mod

Server-side composition root. Production bootstrap, engine hooks, managed byte queues and worker-owned native gRPC host use ABI/schema 2. The separately packaged development harness selects disposable scenarios. Runtime packages exclude harnesses, vendor assemblies and loader assemblies. Deployment and live activation require explicit approval.

Architecture, development policy and evidence live in [Docs](../Docs/ARCHITECTURE.md). Read [contributor instructions](../Docs/AGENTS.md), [module structure](../Docs/MODULE_STRUCTURE.md), [local source overrides](../Docs/LOCAL_SOURCES.md) and [code style](../Docs/CODE_STYLE.md) before changes. Current validation and live deployment procedures are in [the integration report](../Docs/Tasks/SeaPower-TacticalDisplay/IMPLEMENTATION.md).

Build from a VS x64 developer shell with CMake 4.2+, Ninja, and VCPKG_ROOT configured:

```powershell
cmake --preset windows-msvc-release -DFETCHCONTENT_SOURCE_DIR_NCBUILD=/absolute/path/to/Build.CMake
cmake --build o/windows-msvc-release --parallel 8
ctest --test-dir o/windows-msvc-release --output-on-failure
```

Internal dependencies use shared Build.CMake and optional ignored Build.Local.cmake registrations. Windows uses x64-windows-static and static MSVC CRT. Generated artifacts stay under o/. Managed builds additionally require the configured game/reference context; see Docs/MANAGED_BUILD.md. New client repositories are not yet populated remotely, so use local source registrations for the current checkout.

Package targets: NavalCommand.SeaPower.Mod.PackageRuntime and NavalCommand.SeaPower.Mod.PackageHarness. Configuration is disabled by default. Start-Live.ps1 requires explicit deployment approval and always restores its loader configuration and owned plugin directories.

The separate test target `NavalCommand.SeaPower.Mod.LiveFixture` builds a native player-control regression plugin. It is never included in either package. Explicit `Start-Live.ps1 -FixtureAssembly` activation is described in the integration report. Real-game testing identified and corrected paused job-completion starvation; the recorded native ownership, disconnect and reload checks passed.
