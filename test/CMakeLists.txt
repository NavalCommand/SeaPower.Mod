find_package(GTest CONFIG REQUIRED)
add_executable(host-tests NavalCommand/SeaPower/Mod/HostTest.cpp)
target_include_directories(host-tests PRIVATE "${PROJECT_SOURCE_DIR}/src")
target_link_libraries(host-tests PRIVATE SeaPower.Host Gateway.Grpc.Api GTest::gtest_main)
NavalCommandConfigureTarget(host-tests)
add_test(NAME host-abi COMMAND host-tests)
set_tests_properties(host-abi PROPERTIES ENVIRONMENT_MODIFICATION
                                         "PATH=path_list_prepend:$<TARGET_FILE_DIR:SeaPower.Host>")

find_program(DOTNET_EXECUTABLE dotnet REQUIRED)
add_library(abi-mismatch-fixture SHARED NavalCommand/SeaPower/Mod/AbiMismatchFixture.cpp)
target_compile_definitions(abi-mismatch-fixture PRIVATE FIXTURE_ABI_VERSION=2)
NavalCommandConfigureTarget(abi-mismatch-fixture)
add_library(missing-exports-fixture SHARED NavalCommand/SeaPower/Mod/AbiMismatchFixture.cpp)
target_compile_definitions(missing-exports-fixture PRIVATE FIXTURE_ABI_VERSION=1)
NavalCommandConfigureTarget(missing-exports-fixture)
add_test(
    NAME managed-runtime-harness
    COMMAND "${DOTNET_EXECUTABLE}" run --project "${PROJECT_SOURCE_DIR}/test/managed/RuntimeTests.csproj" -c "$<CONFIG>"
        -- "$<TARGET_FILE:abi-mismatch-fixture>" "$<TARGET_FILE:missing-exports-fixture>"
)
