add_library(SeaPower.Host SHARED NavalCommand/SeaPower/Mod/Host.cpp)
target_link_libraries(SeaPower.Host PRIVATE Gateway.Grpc)
NavalCommandConfigureTarget(SeaPower.Host)
