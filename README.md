# AppleTVCrestronDriver

A **Crestron Home** Video Server driver that controls an **Apple TV** over its **Companion Link** protocol, providing pairing, connection status, and remote-control (arrow keys, select, menu, home, play/pause, power) directly from the Crestron Home app.

> **Trademark notice and disclaimer:** Apple, Apple TV, and tvOS are trademarks of Apple Inc., registered in the U.S. and other countries. This project is an independent, unofficial driver and is **not affiliated with, endorsed by, sponsored by, or approved by Apple Inc.** in any way. "Apple TV" and other Apple product names are used solely to describe compatibility and interoperability. No Apple software, assets, or confidential documentation are included in or derived for this repository. Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc.

[![License: MIT + Commons Clause](https://img.shields.io/badge/License-MIT%20%2B%20Commons%20Clause-blue.svg)](LICENSE)

---

> **Two packages, one repository:** This repository hosts two separately installable Crestron Home drivers that are released together under the same version tag: this **Video Server driver** (`CrestronHomeDriver.Apple.AppleTV`), documented below, and a companion **Extension driver** (`CrestronHomeDriver.Apple.AppleTVExtension`) that adds an app-selection and remote-control UI on top of it. Because GitHub only renders one root README, see [AppleTVCrestronExtensionDriver/README.md](AppleTVCrestronExtensionDriver/README.md) for the Extension driver's own installation, configuration, and changelog.

---

## Driver Architecture

This driver is a **Crestron Home Video Server driver**, implemented on the Crestron RAD `ABasicVideoServer` model. It connects to a single Apple TV over the Companion Link protocol using the [AppleTVControlLibrary](https://github.com/oznetmaster/AppleTVControlLibrary) NuGet package for pairing, session, and remote-control functionality, and its companion `AppleTVControlLibrary.Discovery` package for locating the Apple TV on the network by name.

The driver only implements Companion Link (the protocol tvOS remotes and the Apple TV Remote app use for HID input, media transport, volume, and power state). It does not implement MRP, AirPlay 2, RAOP, or DMAP/DACP.

---

## Features

- Pairing directly from the Crestron Home configuration UI: enter the Apple TV's name, start pairing, and enter the on-screen PIN when prompted
- Persisted pairing credentials so the Apple TV does not need to be re-paired after a processor reboot or driver reinitialization
- Automatic reconnection and online/offline status reporting
- Arrow key navigation, Select, Menu, Home, Back
- Discrete power on/off
- Play/Pause transport control

---

## Usage

### Pairing

1. Add a new device in Crestron Home. Search for **Apple TV Companion** in the driver list — Crestron Home lists hundreds of Apple TV drivers, so search by that exact base model name (not just "Apple TV") to find this one.
2. Enter the **Apple TV Name** exactly as it appears under Settings > Remotes and Devices on the Apple TV (this name is used to locate it via Companion Link discovery).
3. Turn on **Pair Now**. A pairing code will appear on the Apple TV screen.
4. Enter that four-digit code into the **Pairing PIN** field to complete pairing.

Once paired, the driver reconnects automatically on subsequent processor reboots without repeating this flow.

> **Video routing reminder:** This driver only handles Companion Link control (pairing, remote input, and power/connection status) — it does not perform any HDMI switching or display control. After adding the driver, make sure you also configure the Apple TV's video routing to a display or switcher device (for example, an HDMI input assignment on a matrix switcher, or a display's source binding) in Crestron Home so that selecting the Apple TV actually switches the display to the correct input.

### Known Issues

- **Configure Pro shows stale attribute descriptions.** The driver updates the Apple TV Name, Pair Now, and Pairing PIN descriptions live to reflect current pairing/connection status. The Crestron Home **Setup** app reflects these updates correctly and immediately. The **Configure Pro** app, however, can display an outdated or default description after reopening a device's configuration page, even though the driver already sent the correct, current description. This appears to be a Configure Pro caching/refresh issue rather than a driver defect; use the Setup app if you need to confirm the current pairing status text.

### Installation

The best way to download and install this driver on a Crestron Home system is to use the [Crestron Home Driver Feed Installer](https://github.com/oznetmaster/Crestron-Home-Driver-Feed-Installer) repository and application.

If you prefer to install manually, use the attached `.pkg` asset from the relevant GitHub Release. The automatic GitHub `Source code (zip)` and `Source code (tar.gz)` assets are repository snapshots, not installable Crestron driver packages.

NuGet package availability: this driver is also published as the `CrestronHomeDriver.Apple.AppleTV` NuGet package. This NuGet package conforms to the **Crestron Home Driver NuGet Publishing Standard v1**. It is a distribution wrapper for the final `.pkg` artifact, includes the required `crestron-driver-package.json` manifest, and is not intended as a direct DLL reference package.

Crestron Home Driver NuGet Publishing Standard v1 is **not** an official Crestron product or specification. It is an open source packaging standard created to facilitate community distribution and discovery of Crestron Home drivers through NuGet.

1. Download the generated `.pkg` asset from the GitHub Release, or build it yourself using the instructions in [Building from Source](#building-from-source).
2. Upload the `.pkg` file to your Crestron Home processor manually (for example via SFTP to `/user/ThirdPartyDrivers/Import`).
3. In the Crestron Home configuration UI, add a new device and select the **Apple TV Companion** driver (search by that exact name, since Crestron Home lists hundreds of Apple TV drivers).
4. Configure the driver as described in [Pairing](#pairing) above.

---

## Building from Source

### Dependencies

- [AppleTVControlLibrary](https://www.nuget.org/packages/AppleTVControlLibrary) NuGet package
- [AppleTVControlLibrary.Discovery](https://www.nuget.org/packages/AppleTVControlLibrary.Discovery) NuGet package
- [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit) NuGet package
- [Crestron.SimplSharp.SDK.Library](https://www.nuget.org/packages/Crestron.SimplSharp.SDK.Library) NuGet package
- `.NET Framework 4.7.2`
- [ILRepack](https://github.com/gluck/il-repack) via `ILRepackMerge.ps1`
- `PatchMergedAssembly.ps1` to rewrite merged assemblies for Crestron Home runtime compatibility
- `ManifestUtil.exe` from the Crestron Driver SDK to produce the final `.pkg`

### Build

```powershell
dotnet build AppleTVCrestronDriver.slnx -c Release
```

The build pipeline:
1. Compiles the driver targeting `net472`
2. Bumps `DriverVersion` and `VersionDate` in `AppleTVCrestronDriver.json`
3. ILRepacks runtime dependencies into the driver assembly
4. Runs `PatchMergedAssembly.ps1` against the merged assembly
5. Packages the driver into a `.pkg` using Crestron's ManifestUtil

### GitHub Release Asset

This repository includes a GitHub Actions workflow that builds the Release package and attaches the generated `.pkg` to a GitHub Release.

The same release workflow builds and publishes **both** packages together from this repository: this driver's `CrestronHomeDriver.Apple.AppleTV` NuGet package and the companion [Extension driver](AppleTVCrestronExtensionDriver/README.md)'s `CrestronHomeDriver.Apple.AppleTVExtension` NuGet package, each wrapping its own generated `.pkg` artifact. The workflow validates that both driver manifests share the same major.minor.release version before building, since the two are released as a single interlocked release.

Typical release flow:
1. Push the release commit and tag (after confirming both driver manifests' `DriverVersion` major.minor.release match)
2. Publish the GitHub Release for that tag
3. Let the workflow build and attach both `.pkg` assets, and publish both NuGet packages, automatically

---

## Testing

`AppleTVCrestronDriver.Tests` is an MSTest unit/integration test project covering the driver logic that
does not require a live Crestron control system or a physical Apple TV. Driver orchestration logic
(pairing, stored-device persistence, connection/reconnect handling, and related state) has been
extracted behind small internal interfaces so it can be exercised directly by this suite. Coverage
includes:

- Pairing state helpers and stored credential persistence
- The Companion Link pairing handshake, driven end-to-end against the in-repo fake device/host
- Bridge protocol command/event tokenization and Base64 text encode/decode round-trips
  (`AppleTvBridgeProtocol`)
- The extracted keyboard focus/text relay bridge (`AppleTvKeyboardBridge`), including on-screen
  keyboard show/hide and live text forwarding, without needing to construct the full Crestron RAD
  base-driver chain

It is **not** a publishable artifact (`IsPackable`/`IsPublishable` are both `false`) and is not part of
the release `.pkg`/NuGet package built above -- it exists purely to validate the driver source in this
repository during development.

### Running the tests

```powershell
dotnet test AppleTVCrestronDriver.slnx
```

### Dependency on AppleTVControlLibrary source

The test project references the in-repo `AppleTVControlLibrary` [FakeDevice test helpers](https://github.com/oznetmaster/AppleTVControlLibrary/tree/master/tests/AppleTV.Companion.FakeDevice)
(`FakeCompanionDevice`/`FakeCompanionTcpHost`) to drive real pairing-handshake integration tests without
sockets to actual hardware. This requires the [AppleTVControlLibrary](https://github.com/oznetmaster/AppleTVControlLibrary)
repository to be cloned as a sibling directory of this repository (i.e. `../AppleTVControlLibrary` relative
to this repository's root), matching the relative paths already used by `AppleTVCrestronDriver.slnx` for
the non-built `AppleTV.Companion`/`AppleTV.Companion.Discovery` source projects.

---

## Repository Notes

- XML documentation generation is enabled in the project build
- The release workflow builds the package on `windows-latest`
- The repository includes the driver package/build scripts needed for packaging and deployment
- The [Extension driver](AppleTVCrestronExtensionDriver/README.md) project links several source files directly from this project (shared credential-lookup and bridge client code) rather than using a `ProjectReference`, so it cannot be built or extracted into a separate repository independently of this one

---

## License

MIT + Commons Clause © 2026 Neil Colvin — see [LICENSE](LICENSE).

Free to use and modify. You may not sell the Software as a standalone product or sublicense it.
Commercial system integration work (for example, a Crestron installer commissioning a customer system) is explicitly permitted, even where a fee is charged for that service.

Apple and Apple TV are trademarks of Apple Inc.

> **Note:** This project references [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit),
> which is subject to Crestron's SDK license agreement. That license governs the SDK libraries only;
> the source code in this repository is licensed independently under the terms above.
