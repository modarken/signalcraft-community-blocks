# =============================================================================
# The vcpkg port.
# =============================================================================
# Header-only, so there is nothing to build: install the headers and the CMake
# config, and let `find_package` do the rest.
#
# vcpkg is ONLY the delivery mechanism here. What a generated SignalCraft
# project contains is `find_package(signalcraft-community-blocks CONFIG
# REQUIRED)` and a link line - plain CMake - so this package works equally from
# anywhere on CMAKE_PREFIX_PATH. That is worth knowing: it means the consuming
# side can be tested without vcpkg, and it is.

vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modarken/signalcraft-community-blocks
    REF "v${VERSION}"
    SHA512 0
    HEAD_REF main
)

vcpkg_cmake_configure(
    SOURCE_PATH "${SOURCE_PATH}/cpp"
)

vcpkg_cmake_install()

vcpkg_cmake_config_fixup(
    PACKAGE_NAME signalcraft-community-blocks
    CONFIG_PATH share/signalcraft-community-blocks
)

# A header-only port installs no libraries, so the debug tree is empty and
# vcpkg would otherwise complain about it.
file(REMOVE_RECURSE "${CURRENT_PACKAGES_DIR}/debug")

vcpkg_install_copyright(FILE_LIST "${SOURCE_PATH}/LICENSE")
