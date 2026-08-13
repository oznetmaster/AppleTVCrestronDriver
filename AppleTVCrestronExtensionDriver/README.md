# AppleTVCrestronExtensionDriver

A **Crestron Home Entity V2 Extension driver** that adds Apple TV app selection and remote-control UI (arrow keys, D-pad, Home/Menu, play/pause/rewind/fast-forward, previous/next track, volume, and on-screen keyboard text entry) to a room, driven over a loopback bridge to the [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver.

> **Trademark notice and disclaimer:** Apple, Apple TV, and tvOS are trademarks of Apple Inc., registered in the U.S. and other countries. This project is an independent, unofficial driver and is **not affiliated with, endorsed by, sponsored by, or approved by Apple Inc.** in any way. "Apple TV" and other Apple product names are used solely to describe compatibility and interoperability. No Apple software, assets, or confidential documentation are included in or derived for this repository. Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc.

[![License: MIT + Commons Clause](https://img.shields.io/badge/License-MIT%20%2B%20Commons%20Clause-blue.svg)](../LICENSE)

---

## Relationship to the Video Server driver

This is an **Extension driver**, not a standalone driver. It requires the [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver to already be configured and paired with the same Apple TV in the room. The extension driver never talks to the Apple TV or Companion Link directly; instead it connects to the Video Server driver's loopback bridge to relay commands (app selection, remote input, volume, keyboard text) and receive status/events (connection state, current app, power state, on-screen keyboard focus).

Because the two drivers are released together and must stay compatible, both are versioned and released from the same [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) repository under the same release tag. See that repository's root [README](https://github.com/oznetmaster/AppleTVCrestronDriver#readme) for the Video Server driver's own installation, pairing, and testing documentation.

> **Not a video-routing device:** This extension driver has no video inputs or outputs of its own and cannot be selected or routed to a display/switcher — only the Video Server driver can be. Before using this extension driver's remote control UI, the Apple TV must already be the active/selected video source in the room (i.e. the Video Server driver's video routing, as described in its own README's [Pairing](https://github.com/oznetmaster/AppleTVCrestronDriver#pairing) section, must already be selected). This extension driver only adds a control surface on top of whatever Apple TV source is already selected; it does not select or switch video sources itself.

---

## Features

- App selection from a live list of installed Apple TV apps, with the currently selected app shown as the tile/list label
- Full remote control: D-pad arrow navigation, Select, Menu, Home (which also resets the app selector back to its default label)
- Discrete power toggle with status label
- Play/Pause, Rewind, Fast Forward, and separate Previous/Next track (skip) buttons
- Volume up/down and mute, shown only when the paired Apple TV/session reports volume control support
- On-screen keyboard text entry: automatically appears when the Apple TV requests text input (e.g. Spotlight search, sign-in) and forwards each keystroke live, matching the behavior of Apple's own Remote app; disappears automatically when the Apple TV dismisses the text field
- Automatically reflects the Video Server driver's connection status, reconnecting its bridge session if the connection drops

---

## Installation

### Prerequisites

1. The [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver must already be installed, added to the room, and successfully paired with the target Apple TV.

### Installing the extension driver

The best way to download and install this driver on a Crestron Home system is to use the [Crestron Home Driver Feed Installer](https://github.com/oznetmaster/Crestron-Home-Driver-Feed-Installer) repository and application.

If you prefer to install manually, use the attached `.pkg` asset from the relevant GitHub Release (the same release that published the paired Video Server driver's `.pkg`). The automatic GitHub `Source code (zip)` and `Source code (tar.gz)` assets are repository snapshots, not installable Crestron driver packages.

NuGet package availability: this driver is also published as the `CrestronHomeDriver.Apple.AppleTVExtension` NuGet package. This NuGet package conforms to the **Crestron Home Driver NuGet Publishing Standard v1**. It is a distribution wrapper for the final `.pkg` artifact, includes the required `crestron-driver-package.json` manifest, and is not intended as a direct DLL reference package.

1. Download the generated `.pkg` asset from the GitHub Release, or build it yourself using the instructions in [Building from Source](#building-from-source).
2. Upload the `.pkg` file to your Crestron Home processor manually (for example via SFTP to `/user/ThirdPartyDrivers/Import`).
3. In the Crestron Home configuration UI, add this extension driver to the same room as the paired Apple TV Companion Video Server driver, and enter the same **Apple TV Name** in its configuration as configured on that Video Server driver (see [Configuration](#configuration)).

---

## Configuration

This extension driver has a single required configuration field:

- **Apple TV Name** — the same Apple TV name configured on the paired [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver (the exact name entered there during [Pairing](https://github.com/oznetmaster/AppleTVCrestronDriver#pairing)). This driver never pairs with or connects to the Apple TV itself — it uses this name only to look up that Video Server driver's stored device and connect to its loopback bridge, which owns the single live Companion Link session for that Apple TV.

Once the Apple TV Name is set and matches a configured Video Server driver instance, this extension driver automatically connects to that driver's loopback bridge and reconnects automatically if the bridge connection is interrupted.

> **Room without a UI device:** This extension driver only adds an on-screen control surface. Rooms without any UI-capable device (for example, a room controlled solely by a handheld remote) can still include the paired Video Server driver for connection/pairing and basic remote control; adding this extension driver in that case simply has no visible effect and causes no errors.

---

## Building from Source

### Dependencies

- [AppleTVControlLibrary](https://www.nuget.org/packages/AppleTVControlLibrary) NuGet package
- [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit) NuGet package
- [Crestron.SimplSharp.SDK.Library](https://www.nuget.org/packages/Crestron.SimplSharp.SDK.Library) NuGet package
- `.NET Framework 4.7.2`
- [ILRepack](https://github.com/gluck/il-repack) via `ILRepackMerge.ps1`
- `PatchMergedAssembly.ps1` to rewrite merged assemblies for Crestron Home runtime compatibility
- `ManifestUtil.exe` from the Crestron Driver SDK to produce the final `.pkg`

> **Not independently buildable:** This project does not use a `ProjectReference` to
> `AppleTVCrestronDriver.csproj` (that would pull in a second, separately ILRepack-merged copy of
> `AppleTVControlLibrary`'s public types, causing `CS0433` duplicate-type errors). Instead it links
> several source files directly from the sibling `AppleTVCrestronDriver` project
> (`SharedStorage.cs`, `ICredentialFileStore.cs`, `CrestronCredentialFileStore.cs`,
> `AppleTvStoredDevice.cs`, `AppleTvBridgeProtocol.cs`, `AppleTvBridgePort.cs`,
> `AppleTvBridgeClient.cs`) so it can look up that driver's stored Apple TV device and talk to its
> loopback bridge. Because of this, `AppleTVCrestronDriver` must remain present as a sibling
> directory/project in this repository and solution — this extension driver cannot be built, or
> extracted into its own repository, on its own.

### Build

```powershell
dotnet build AppleTVCrestronDriver.slnx -c Release
```

The build pipeline compiles this project targeting `net472`, bumps `DriverVersion`/`VersionDate` in `AppleTVCrestronExtensionDriver.json`, merges dependencies via ILRepack, and produces the final `.pkg` via `ManifestUtil.exe`, mirroring the Video Server driver's own build pipeline described in its [README](https://github.com/oznetmaster/AppleTVCrestronDriver#building-from-source).

---

## Release Notes

See [CHANGELOG.md](CHANGELOG.md) for this package's release history.

---

## License

MIT + Commons Clause (c) 2026 Neil Colvin -- see [LICENSE](../LICENSE).

Free to use and modify. You may not sell the Software as a standalone product or sublicense it.
Commercial system integration work (for example, a Crestron installer commissioning a customer system) is explicitly permitted, even where a fee is charged for that service.

Apple and Apple TV are trademarks of Apple Inc.

> **Note:** This project references [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit),
> which is subject to Crestron's SDK license agreement. That license governs the SDK libraries only;
> the source code in this repository is licensed independently under the terms above.