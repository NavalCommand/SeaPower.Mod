
add_library(NavalCommand.SeaPower.Mod SHARED NavalCommand/SeaPower/Mod/Host.cpp)
target_link_libraries(NavalCommand.SeaPower.Mod PRIVATE NavalCommand.Gateway.Grpc)
NavalCommandConfigureTarget(NavalCommand.SeaPower.Mod)
set_target_properties(NavalCommand.SeaPower.Mod PROPERTIES OUTPUT_NAME "SeaPower.Host")

target_include_directories(NavalCommand.SeaPower.Mod PRIVATE "${PROJECT_SOURCE_DIR}/src")
