cmake_minimum_required(VERSION 3.28)
project(data_mover)
set(CMAKE_CXX_STANDARD 20)
add_compile_options(-Wall -Wno-deprecated-declarations)

include_directories(${CMAKE_CURRENT_SOURCE_DIR})
include(FetchContent)

# Fetch AES library
FetchContent_Declare(
        aes
        GIT_REPOSITORY https://github.com/SergeyBel/AES.git
        GIT_TAG v7.6.1
)
FetchContent_MakeAvailable(aes)
include_directories(${aes_SOURCE_DIR}/src)

# Fetch TOML file library
FetchContent_Declare(
    tomlplusplus
    GIT_REPOSITORY https://github.com/marzer/tomlplusplus.git
    GIT_TAG v3.4.0
)
FetchContent_MakeAvailable(tomlplusplus)
include_directories(${tomlplusplus_SOURCE_DIR}/include)

# Fetch PostgreSQL client library (must be master to get CMake 3.30.0 fix)
FetchContent_Declare(
    libpqxx
    GIT_REPOSITORY https://github.com/jtv/libpqxx.git
    GIT_TAG master
)
FetchContent_MakeAvailable(libpqxx)
include_directories(${libpqxx_SOURCE_DIR}/include)
include_directories(${libpqxx_BINARY_DIR}/include)

# Compile and link data_mover application
add_executable(
    data_mover
    data_mover.cc
    data_mover_actions.hh
    data_mover_actions.cc
    configuration/data_mover_config.cc
    configuration/database_config.hh
    configuration/database_config.cc
    configuration/table_config.hh
    configuration/table_config.cc
    configuration/column_config.hh
    configuration/column_config.cc
    processing/data_mover_processors.hh
    processing/data_mover_processors.cc
    processing/abstract_processor.hh
    processing/hello_world_processor.hh
    processing/hello_world_processor.cc
    processing/encryption_processor.hh
    processing/encryption_processor.cc
    structure/data_mover_structure.hh
    structure/data_mover_structure.cc
    structure/database_structure.hh
    structure/database_structure.cc
    structure/schema_structure.hh
    structure/schema_structure.cc
    structure/table_structure.hh
    structure/table_structure.cc
    structure/column_structure.hh
    structure/column_structure.cc
    utility/connection_from_config.hh
    utility/connection_from_config.cc
)
target_link_libraries(
    data_mover
    AES
    tomlplusplus::tomlplusplus
    libpqxx::pqxx
)


