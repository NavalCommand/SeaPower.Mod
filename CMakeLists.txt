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

NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Api.git fe5ab9f1c30e275bcf98757a74609baa2f989694)
NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Grpc.Api.git 3b60b8cafd441d50ef7d333ee94004254a0b0ffc)
NavalCommandAddDependency(https://github.com/NavalCommand/Gateway.Grpc.git 981a1c33a7093f209695a3a0a1729c9223dc0e84)
NavalCommandAddDependency(https://github.com/NavalCommand/SeaPower.Client.git 6b940819841a6ca6bfa4d769ac363021b561dba7)

NavalCommandConfigureModule()
