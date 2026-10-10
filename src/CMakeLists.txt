
add_library(SeaPower.Mod SHARED NavalCommand/SeaPower/Mod/Host.cpp)
target_link_libraries(SeaPower.Mod PRIVATE Gateway.Grpc)
NavalCommandConfigureTarget(SeaPower.Mod)
set_target_properties(SeaPower.Mod PROPERTIES OUTPUT_NAME "SeaPower.Host")

target_include_directories(SeaPower.Mod PRIVATE "${PROJECT_SOURCE_DIR}/src")
