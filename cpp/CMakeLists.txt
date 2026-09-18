# =============================================================================
# The C++ half of this library.
# =============================================================================
# A header-only CMake package. `find_package(signalcraft-community-blocks
# CONFIG REQUIRED)` finds it, and a generated SignalCraft project links
# `signalcraft-community-blocks::blocks`.
#
# THAT TARGET NAME IS WHAT A DESIGN HAS TO STATE. A vcpkg port's name and the
# CMake target it exports are unrelated strings - both chosen here, and nothing
# on the consuming side can derive one from the other - so a design importing
# this through vcpkg says:
#
#   { "name": "signalcraft-community-blocks", "kind": "vcpkg",
#     "version": "1.0.0", "target": "signalcraft-community-blocks::blocks" }
#
# vcpkg is only the delivery mechanism. What SignalCraft generates is plain
# CMake, so this package can equally be installed anywhere on CMAKE_PREFIX_PATH
# and consumed with no vcpkg involved at all - which is also how it is tested.

cmake_minimum_required(VERSION 3.24)
project(signalcraft-community-blocks VERSION 1.0.0 LANGUAGES CXX)

include(GNUInstallDirs)
include(CMakePackageConfigHelpers)

# Header-only: the block is a class in a header, like most small DSP blocks.
add_library(blocks INTERFACE)
add_library(signalcraft-community-blocks::blocks ALIAS blocks)

target_include_directories(blocks INTERFACE
  $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
  $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>)

target_compile_features(blocks INTERFACE cxx_std_23)

# SignalCraft's engine tier, for core::IBlock. Only the engine tier - a block
# has no business with the toolchain, the artifact loader or the authoring
# registry (ADR-010 rule 3).
find_package(SignalCraft CONFIG QUIET)
if(SignalCraft_FOUND)
  target_link_libraries(blocks INTERFACE SignalCraft::signalcraft_engine)
endif()

install(TARGETS blocks EXPORT community-blocks-targets)
install(DIRECTORY include/ DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})

install(EXPORT community-blocks-targets
        FILE signalcraft-community-blocks-targets.cmake
        NAMESPACE signalcraft-community-blocks::
        DESTINATION ${CMAKE_INSTALL_DATADIR}/signalcraft-community-blocks)

configure_package_config_file(
  "${CMAKE_CURRENT_SOURCE_DIR}/signalcraft-community-blocks-config.cmake.in"
  "${CMAKE_CURRENT_BINARY_DIR}/signalcraft-community-blocks-config.cmake"
  INSTALL_DESTINATION ${CMAKE_INSTALL_DATADIR}/signalcraft-community-blocks)

write_basic_package_version_file(
  "${CMAKE_CURRENT_BINARY_DIR}/signalcraft-community-blocks-config-version.cmake"
  COMPATIBILITY SameMajorVersion
  ARCH_INDEPENDENT)

install(FILES
  "${CMAKE_CURRENT_BINARY_DIR}/signalcraft-community-blocks-config.cmake"
  "${CMAKE_CURRENT_BINARY_DIR}/signalcraft-community-blocks-config-version.cmake"
  DESTINATION ${CMAKE_INSTALL_DATADIR}/signalcraft-community-blocks)
