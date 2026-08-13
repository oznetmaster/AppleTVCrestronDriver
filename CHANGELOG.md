# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This changelog covers the `CrestronHomeDriver.Apple.AppleTV` package. See the paired
[AppleTVCrestronExtensionDriver changelog](AppleTVCrestronExtensionDriver/CHANGELOG.md) for the
Extension driver's release history. Both packages are released together from this repository
under the same version tag.

## [1.3.1] - 2026-08-13

### Changed

- Removed the extension driver's build-time dependency on `AppleTVControlLibrary`. The
  Companion Link credential conversion (`ToCredentials()`) that only the video server driver
  needs was moved out of the shared `AppleTvStoredDevice` model into a video-server-only
  extension method, so the extension driver package no longer references or merges
  `AppleTvControlLibrary.dll`. No behavior change for either driver.

## [1.3.0] - 2026-08-13

### Added

- Added `SupportsForwardScan`/`SupportsReverseScan` (rewind/fast-forward) and
  `SupportsForwardSkip`/`SupportsReverseSkip` (previous/next track) support to the driver
  manifest, matching the corresponding rewind/fast-forward and skip commands the driver already
  implements.

### Changed

- Synchronized versioning with the new companion `CrestronHomeDriver.Apple.AppleTVExtension`
  extension driver package, which is now released together with this driver from this repository
  under the same version tag. See the [Extension driver's changelog](AppleTVCrestronExtensionDriver/CHANGELOG.md)
  for its own release notes.

## [1.1.2] - 2026-08-11

### Fixed

- Changing `AppleTvName` could leave a stale, now-superseded driver instance's discovery/connect pass running concurrently with the new instance's own pass, occasionally producing redundant or overlapping status updates for what was a single user edit. The prior configure pass is now cancelled when the driver reinitializes for a name change.

### Known Issues

- Crestron Home's Configure Pro app can display stale/incorrect user attribute descriptions (e.g. Apple TV Name, Pair Now) after reopening the device configuration page, even though the driver has already sent the correct, current description. Using the Crestron Home Setup app instead shows the descriptions correctly and updates them immediately when values change. This appears to be a Configure Pro caching/refresh issue rather than a driver defect.

## [1.1.1] - 2026-08-10

### Fixed

- Power On no longer does nothing: Wake is now sent as a single button-up HID event instead of a down+up pair, which the Apple TV silently ignored.
- Power state changes made externally (e.g. from the Apple TV Remote or another controller) are now reflected in Crestron Home instead of being received and discarded.
- Pair Now could fail with "Frame transport failed; the session has been faulted" and silently ignore a subsequently entered PIN: a stale, concurrently-running saved-endpoint recovery pass could tear down the TCP session an in-flight pairing handshake was still using, faulting it mid-verification.
- A pairing completed via Pair Now while saved-endpoint recovery was already in progress for the same Apple TV could have its freshly saved credentials immediately overwritten by that recovery pass reconnecting with the older, stale credentials, leaving the device unable to reconnect until Pair Now was run again.
- Every driver initialization ran the saved-endpoint connect/discovery pass twice concurrently: once from the configured Apple TV name being replayed through `SetUserAttribute`, and once from an explicit, now-redundant call at the end of `Initialize()`.

### Changed

- Diagnostic logging now routes through the RAD base class's `Log()` method, gated on `EnableLogging`, so it is visible via Crestron Home's logging toggle in the field; direct `ErrorLog` output is now emitted only in Debug builds.

## [1.1.0] - 2026-08-09

### Changed

- Removed unimplemented Exit/Info command support (`SupportsExit`/`SupportsInfo` set to `false` in the driver manifest); these were never wired up to a Companion Link command.

## [1.0.0] - 2026-08-06

### Added

- Initial public release of the Crestron Home Video Server driver for Apple TV over Companion Link.
- Pairing flow driven from the Crestron Home configuration UI (Apple TV Name, Pair Now, Pairing PIN).
- Persisted pairing credentials across driver/processor reinitialization.
- Automatic reconnection with online/offline status reporting.
- Remote control support: arrow keys, Select, Menu, Home, Back, discrete power, and Play/Pause.
