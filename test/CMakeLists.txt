find_package(GTest CONFIG REQUIRED)
add_executable(host-tests NavalCommand/SeaPower/Mod/HostTest.cpp)
target_include_directories(host-tests PRIVATE "${PROJECT_SOURCE_DIR}/src")
target_link_libraries(host-tests PRIVATE SeaPower.Host Gateway.Grpc.Api GTest::gtest_main)
NavalCommandConfigureTarget(host-tests)
add_test(NAME host-abi COMMAND host-tests)
set_tests_properties(host-abi PROPERTIES ENVIRONMENT_MODIFICATION
                                         "PATH=path_list_prepend:$<TARGET_FILE_DIR:SeaPower.Host>")
