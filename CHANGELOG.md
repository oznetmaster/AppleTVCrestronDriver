# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.1.0] - 2026

### Changed

- Removed unimplemented Exit/Info command support (`SupportsExit`/`SupportsInfo` set to `false` in the driver manifest); these were never wired up to a Companion Link command.

## [1.0.0] - 2026

### Added

- Initial public release of the Crestron Home Video Server driver for Apple TV over Companion Link.
- Pairing flow driven from the Crestron Home configuration UI (Apple TV Name, Pair Now, Pairing PIN).
- Persisted pairing credentials across driver/processor reinitialization.
- Automatic reconnection with online/offline status reporting.
- Remote control support: arrow keys, Select, Menu, Home, Back, discrete power, and Play/Pause.
