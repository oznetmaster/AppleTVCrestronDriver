# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

This changelog covers the `CrestronHomeDriver.Apple.AppleTVExtension` package. See the paired
[AppleTVCrestronDriver changelog](../CHANGELOG.md) for the Video Server driver's release history.
Both packages are released together from this repository under the same version tag.

## [1.3.1] - 2026-08-13

### Changed

- No longer depends on `AppleTVControlLibrary`. The shared `AppleTvStoredDevice` model this
  driver links now only exposes the fields/lookup it actually needs (device name, address,
  port, unique id); the Companion Link credential conversion used solely by the paired video
  server driver was moved into that driver's own project. This reduces the extension driver's
  merged package size and removes an unnecessary dependency. No functional change.

## [1.3.0] - 2026-08-13

### Added

- On-screen keyboard text entry, mirroring the Apple TV Remote app: automatically shown when the
  Apple TV requests text input and forwards each keystroke live; hidden automatically when the
  Apple TV dismisses the text field.
- Separate Previous/Next track (skip) buttons (`icPrevious`/`icNext`), distinct from Rewind/Fast
  Forward, mapped to the underlying previous/next track commands.

### Changed

- Renamed the media buttongroup's skip buttons to reflect their actual behavior (`Rewind`/`Fast
  Forward` for continuous scanning), separate from the new discrete Previous/Next track buttons.
- The Home button now also resets the app selector back to its default label.
- Versioning is now synchronized with the companion `CrestronHomeDriver.Apple.AppleTV` Video
  Server driver package; both are released together from this repository under the same version
  tag going forward.

## [1.0.0] - 2026-08-06

### Added

- Initial public release of the Crestron Home Entity V2 Extension driver for Apple TV, providing
  app selection and remote-control UI driven over a loopback bridge to the paired
  AppleTVCrestronDriver Video Server driver.
