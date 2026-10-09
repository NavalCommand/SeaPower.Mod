NavalCommandGetDependencySource(https://github.com/NavalCommand/Gateway.Api.git apiSource)
NavalCommandGetDependencySource(https://github.com/NavalCommand/SeaPower.Client.git clientSource)
find_package(Protobuf CONFIG REQUIRED)
file(
    GENERATE
    OUTPUT "${CMAKE_BINARY_DIR}/managed-paths.json"
    CONTENT
        "{\"api\":\"${apiSource}\",\"client\":\"${clientSource}\",\"protoc\":\"$<TARGET_FILE:protobuf::protoc>\",\"vcpkg\":\"${VCPKG_INSTALLED_DIR}\",\"triplet\":\"${VCPKG_TARGET_TRIPLET}\"}"
)
