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

# The hash is of the release tarball GitHub serves for the tag, and it can only
# be known once that tag exists. vcpkg checks it on every install: a port whose
# source changed under it is refused rather than built, which is the point.
#
# When the version moves, this moves with it. `vcpkg install` with a wrong hash
# prints the one it actually got, so there is no guessing involved.
vcpkg_from_github(
    OUT_SOURCE_PATH SOURCE_PATH
    REPO modarken/signalcraft-community-blocks
    REF "v${VERSION}"
    SHA512 b919c58f4afb30ee5bdce726d8258d0adc341e615ed41ea2adda74eaa7e8f4f43539713f0c614eb1b89edd94473e9b4a436af22127e1bb56e30f0c94d97517e1
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
