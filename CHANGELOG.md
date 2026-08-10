# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.1.1] - 2026-08-10

### Fixed

- Power On no longer does nothing: Wake is now sent as a single button-up HID event instead of a down+up pair, which the Apple TV silently ignored.
- Power state changes made externally (e.g. from the Apple TV Remote or another controller) are now reflected in Crestron Home instead of being received and discarded.

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
