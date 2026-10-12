find_package(GTest CONFIG REQUIRED)
include(GoogleTest)
add_executable(NavalCommand.SeaPower.Mod.Test NavalCommand/SeaPower/Mod/HostTest.cpp)
target_include_directories(NavalCommand.SeaPower.Mod.Test PRIVATE "${PROJECT_SOURCE_DIR}/src")
target_link_libraries(NavalCommand.SeaPower.Mod.Test PRIVATE NavalCommand.SeaPower.Mod NavalCommand.Gateway.Grpc GTest::gtest_main)
NavalCommandConfigureTarget(NavalCommand.SeaPower.Mod.Test)
add_custom_command(TARGET NavalCommand.SeaPower.Mod.Test POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
    "$<TARGET_FILE:NavalCommand.SeaPower.Mod>" "$<TARGET_FILE_DIR:NavalCommand.SeaPower.Mod.Test>")
gtest_discover_tests(NavalCommand.SeaPower.Mod.Test DISCOVERY_MODE PRE_TEST)
find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
add_test(NAME NavalCommand.SeaPower.Mod.ManagedTest
    COMMAND "${DOTNET_EXECUTABLE}" run --project "${CMAKE_CURRENT_SOURCE_DIR}/managed/RuntimeTests.csproj" --configuration "$<CONFIG>")
