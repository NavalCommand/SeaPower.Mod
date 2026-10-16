cmake_minimum_required(VERSION 4.2)
cmake_policy(SET CMP0168 NEW)
project(
    SeaPower.Mod
    VERSION 0.1.0
    LANGUAGES CXX)

include(FetchContent)
FetchContent_Declare(
    ncbuild
    GIT_REPOSITORY https://github.com/NavalCommand/Build.CMake.git
    GIT_TAG main)
FetchContent_MakeAvailable(ncbuild)
include("${ncbuild_SOURCE_DIR}/NavalCommand.Build.CMake.cmake")

NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Api.git 406e3f76f4b58a473ef21767d28bf92f65d725cf)
NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Grpc.Api.git 3c0a2a682d6f7a595f7d31717707d12d9c2770ba)
NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Grpc.git 407ecb7c82692181a71a8189a8a19a53a98d2084)
NavalCommandAddDependency(https://github.com/NavalCommand/SeaPower.Client.git ca28d2a8397ae4f811218c14668e8c4ecc7c52da)

NavalCommandConfigureModule()
