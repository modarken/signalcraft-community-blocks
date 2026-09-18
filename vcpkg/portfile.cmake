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
    SHA512 9c407afd815cbc36ef8437b105ac658936f7131bb53b5a316d501b0b078a32211ec9402677db9395f31e23ca098a3f233bfaf957219e5358ffccb7a43f8cfed8
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
