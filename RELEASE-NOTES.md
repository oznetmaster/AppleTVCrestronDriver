# AppleTVCrestronDriver v1.4.3

Patch release correcting lifecycle, configuration and recovery defects while preserving the public API and intended driver behavior.

## Fixes

- Cancel superseded discovery and connection attempts before they can persist stale identity or report an obsolete paired state.
- Initialize the extension app list as an empty collection. Clearing its configuration also clears apps, selected app, keyboard state, volume capability and power state.
- Add regression coverage for saved pairing, shared credential restoration, configured-name changes, reconnect recovery and extension configuration cleanup.

## Tests and build process

- 105 offline tests and 11 SDK lifecycle tests. The current implementation passes on Windows in Debug and Release; both processor suites passed twice in the same host process.
- The shared net472 processor test package is available in the solution and appears under **Utility** in Configure. Its standalone Home tile and Windows NUnit runner select the test suites.
- Driver Debug build versions follow the manifest; three-part release tags select the CI release version. Test builds do not increment or deploy the production driver.
- Processor test packages are not published to NuGet. Private deployment settings, live inputs and desktop SDK runtime dependencies are excluded from source and release assets.

## Installation and documentation

The GitHub release includes the production driver package and a separate processor test package. The test package appears under Utility in Configure and is not included in the driver NuGet package. See [CHANGELOG.md](CHANGELOG.md) for release history and [README.md](README.md) for installation and testing.

The `1.4.2` publication attempt stopped before producing driver binaries or publishing NuGet because an existing Git exclusion omitted the required versioning script. Version `1.4.3` includes that script and the fixes above; the earlier tag is preserved.
