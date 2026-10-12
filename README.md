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

For interactive mission testing, run `pwsh -File ./scripts/Start-TestMission.ps1`. Choose a numbered mission from `managed/harness/scenarios`; the script reads its player side, creates a fresh log directory under `o/live/`, and loads both staged mods. It discovers the game directory from the local managed build context, or accepts `-GameRoot`. Invoking this launcher authorizes temporary deployment and loader activation. Keep its PowerShell window open and quit Sea Power normally when finished; cleanup then restores the loader and removes temporary plugins. CommandCenter can be launched separately at any time during the session. Packages must already be built.

Use `-ScenarioName NearContact` to skip the menu, or add `-WhatIf` to preview without deploying or starting the game. `Start-Live.ps1 -WaitForGameExit` provides the same interactive session lifetime for direct invocations; its default bounded verification behavior is unchanged.

Running `./scripts/Start-TestMission.ps1` in Windows PowerShell 5.1 automatically hands off to PowerShell 7, preserving supplied options. It looks for `pwsh` on PATH, a standard installation, or the existing Codex bundled runtime. If none is available it reports the requirement before deployment.

The separate test target `NavalCommand.SeaPower.Mod.LiveFixture` builds a native player-control regression plugin. It is never included in either package. Explicit `Start-Live.ps1 -FixtureAssembly` activation is described in the integration report. Real-game testing identified and corrected paused job-completion starvation; the recorded native ownership, disconnect and reload checks passed.
