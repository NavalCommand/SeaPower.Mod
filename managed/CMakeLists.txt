NavalCommandGetDependencySource(https://github.com/NavalCommand/Gateway.Api.git apiSource)
NavalCommandGetDependencySource(https://github.com/NavalCommand/SeaPower.Client.git clientSource)
NavalCommandManagedProtoc(protocPath)
set(gameManaged "")
if (SEAPOWER_GAME_ROOT)
    set(gameManaged "${SEAPOWER_GAME_ROOT}/Sea Power_Data/Managed")
endif ()
NavalCommandManagedContext("GatewayApiSource=${apiSource}" "SeaPowerClientSource=${clientSource}"
        "Protoc=${protocPath}" "GameManaged=${gameManaged}")
NavalCommandAddManagedProject(managed-runtime runtime/SeaPower.Mod.csproj)
add_dependencies(managed-runtime SeaPower.Host)
NavalCommandAddManagedProject(managed-harness harness/SeaPower.Harness.csproj)
if (PROJECT_IS_TOP_LEVEL AND BUILD_TESTING)
    NavalCommandAddManagedProject(managed-tests "../test/managed/RuntimeTests.csproj")
endif ()

find_program(POWERSHELL_EXECUTABLE pwsh REQUIRED)
add_custom_target(package-runtime
        COMMAND "${POWERSHELL_EXECUTABLE}" -NoProfile -File "${PROJECT_SOURCE_DIR}/scripts/Package.ps1"
        -Kind Runtime -Configuration "$<CONFIG>" -NativeHost "$<TARGET_FILE:SeaPower.Host>"
        -ApiSource "${apiSource}" -VcpkgInstalled "${VCPKG_INSTALLED_DIR}" -Triplet "${VCPKG_TARGET_TRIPLET}"
        DEPENDS SeaPower.Host managed-runtime
        VERBATIM)
add_custom_target(package-harness
        COMMAND "${POWERSHELL_EXECUTABLE}" -NoProfile -File "${PROJECT_SOURCE_DIR}/scripts/Package.ps1"
        -Kind Harness -Configuration "$<CONFIG>"
        DEPENDS managed-harness
        VERBATIM)
