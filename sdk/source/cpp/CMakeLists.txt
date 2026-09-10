cmake_minimum_required(VERSION 3.16)
project(dsn_source VERSION 0.1.0 LANGUAGES CXX)
if(NOT CMAKE_SYSTEM_NAME MATCHES "^(Linux|Android)$")
  message(FATAL_ERROR "dsn_source currently supports Linux and Android/Termux")
endif()
option(DSN_BUILD_TESTS "Build repository SDK tests" OFF)
if(DSN_BUILD_TESTS)
  add_executable(source_test ../../../tests/sdk/source_test.cpp)
  target_link_libraries(source_test PRIVATE dsn::source)
  target_compile_options(source_test PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()
find_package(Threads REQUIRED)
add_library(dsn_source STATIC src/source.cpp)
add_library(dsn::source ALIAS dsn_source)
target_compile_features(dsn_source PUBLIC cxx_std_17)
target_include_directories(dsn_source PUBLIC
  $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
  $<INSTALL_INTERFACE:include>)
target_link_libraries(dsn_source PUBLIC Threads::Threads)
target_compile_options(dsn_source PRIVATE -Wall -Wextra -Wpedantic -Werror)
set_target_properties(dsn_source PROPERTIES EXPORT_NAME source)

include(GNUInstallDirs)
include(CMakePackageConfigHelpers)
configure_package_config_file(cmake/dsn_sourceConfig.cmake.in
  ${CMAKE_CURRENT_BINARY_DIR}/dsn_sourceConfig.cmake
  INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/dsn_source)
write_basic_package_version_file(${CMAKE_CURRENT_BINARY_DIR}/dsn_sourceConfigVersion.cmake
  COMPATIBILITY SameMajorVersion)
install(TARGETS dsn_source EXPORT dsn_sourceTargets ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(DIRECTORY include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(EXPORT dsn_sourceTargets NAMESPACE dsn:: DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/dsn_source)
install(FILES ${CMAKE_CURRENT_BINARY_DIR}/dsn_sourceConfig.cmake
  ${CMAKE_CURRENT_BINARY_DIR}/dsn_sourceConfigVersion.cmake DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/dsn_source)

option(DSN_BUILD_SAMPLES "Build repository examples" OFF)
if(DSN_BUILD_SAMPLES)
  add_executable(temperature_source ../../../samples/temperature/source.cpp)
  target_link_libraries(temperature_source PRIVATE dsn::source)
endif()
