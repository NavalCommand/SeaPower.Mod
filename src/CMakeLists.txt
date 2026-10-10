
add_library(SeaPower.Mod SHARED NavalCommand/SeaPower/Mod/Host.cpp)
target_link_libraries(SeaPower.Mod PRIVATE Gateway.Grpc)
NavalCommandConfigureTarget(SeaPower.Mod)

target_include_directories(SeaPower.Mod PRIVATE "${PROJECT_SOURCE_DIR}/src")
