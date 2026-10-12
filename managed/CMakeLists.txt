NavalCommandGetDependencySource(https://github.com/NavalCommand/Gateway.Api.git apiSource)
NavalCommandGetDependencySource(https://github.com/NavalCommand/SeaPower.Client.git clientSource)
NavalCommandManagedProtoc(protocPath)
set(gameManaged "")
if (SEAPOWER_GAME_ROOT)
    set(gameManaged "${SEAPOWER_GAME_ROOT}/Sea Power_Data/Managed")
endif ()
NavalCommandManagedContext("GatewayApiSource=${apiSource}" "SeaPowerClientSource=${clientSource}"
        "Protoc=${protocPath}" "GameManaged=${gameManaged}")
NavalCommandAddManagedProject(NavalCommand.SeaPower.Mod.Managed runtime/SeaPower.Mod.csproj)
add_dependencies(NavalCommand.SeaPower.Mod.Managed NavalCommand.SeaPower.Mod)
NavalCommandAddManagedProject(NavalCommand.SeaPower.Mod.Harness harness/SeaPower.Harness.csproj)
if (PROJECT_IS_TOP_LEVEL AND BUILD_TESTING)
    NavalCommandAddManagedProject(NavalCommand.SeaPower.Mod.ManagedTest "../test/managed/RuntimeTests.csproj")
endif ()

find_program(POWERSHELL_EXECUTABLE pwsh REQUIRED)
add_custom_target(NavalCommand.SeaPower.Mod.PackageRuntime
        COMMAND "${POWERSHELL_EXECUTABLE}" -NoProfile -File "${PROJECT_SOURCE_DIR}/scripts/Package.ps1"
        -Kind Runtime -Configuration "$<CONFIG>" -NativeHost "$<TARGET_FILE:NavalCommand.SeaPower.Mod>"
        -ApiSource "${apiSource}" -VcpkgInstalled "${VCPKG_INSTALLED_DIR}" -Triplet "${VCPKG_TARGET_TRIPLET}"
        DEPENDS NavalCommand.SeaPower.Mod NavalCommand.SeaPower.Mod.Managed
        VERBATIM)
add_custom_target(NavalCommand.SeaPower.Mod.PackageHarness
        COMMAND "${POWERSHELL_EXECUTABLE}" -NoProfile -File "${PROJECT_SOURCE_DIR}/scripts/Package.ps1"
        -Kind Harness -Configuration "$<CONFIG>"
        DEPENDS NavalCommand.SeaPower.Mod.Harness
        VERBATIM)
