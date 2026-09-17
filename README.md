# AppleTVCrestronDriver

For shipped changes, see the [changelog](CHANGELOG.md). Test, CI and build history is recorded separately in [development and validation history](DEVELOPMENT-HISTORY.md).


See the [changelog](CHANGELOG.md) for release history and the [release notes](RELEASE-NOTES.md) for the current driver update. Driver releases are made for runtime fixes or dependency changes; adding tests alone does not require a driver release.

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

`AppleTVCrestronDriver.Tests` is a NUnit unit/integration test project with 105 tests covering the driver logic that
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
dotnet test AppleTVCrestronDriver.Tests/AppleTVCrestronDriver.Tests.csproj -c Debug
```

Visual Studio Test Explorer uses the NUnit adapter. Tests target `net472`, use a fresh fixture
instance for each test, and run serially to preserve the existing test isolation. Test project builds
merge the driver dependencies but skip production driver version bumps, packaging and deployment.

### Running the same suite on Crestron Home

The solution also includes [AppleTVCrestronDriver.ProcessorTests](AppleTVCrestronDriver.ProcessorTests/README.md).
Build that project in **Debug** in Visual Studio to create and deploy the standalone
**AppleTVCrestronDriver Tests** package, using private local deployment settings. Find it in
the **Utility** category in Crestron Home Configure. Run its 105 unit tests and 11 processor lifecycle tests from its tile or discover
the package in the Windows NUnit runner. This suite uses simulated Apple TV services and loopback
connections; it needs no Apple TV pairing credentials or live-test settings.

The test package has its own identity and test tile. The production drivers' UIs are not installed
by it. Its `.pkg` is a separate development artifact, not a NuGet package and not part of the
production driver release workflow. Processor packaging also requires a sibling
[CrestronHomeNUnit](https://github.com/oznetmaster/CrestronHomeNUnit) checkout.

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


### Expanded driver behavior tests

Cover saved pairing, shared credential restoration, discovery identity persistence, configured-name changes, retry recovery and superseded discovery/connection attempts. Cancellation prevents old connection attempts from persisting identity or reporting a paired state for a newer configuration.

Real extension entities publish bridge events, maintain connection and tile state, clear keyboard text when focus is lost, reject missing device names before storage access, and clear device-specific UI state when configuration is removed.

The current package contains **105 offline tests** and **11 SDK entity/lifecycle tests**. The processor package remains **net472 only**, appears under **Utility** in Configure, and can run independently through its own tile or the Windows NUnit runner. These fixtures use synthetic data and do not operate installed devices or authenticate with real accounts.

`AppleTVCrestronDriver.Lifecycle.Tests` runs the entity checks against the real desktop SDK on .NET 10. It compiles the relevant driver sources and shares fixture sources with the net472 processor tests. Building this project does not deploy a driver. A locally supplied `Newtonsoft.Json.Compact.dll` is needed by the SDK's manifest reader; it is supplied by the processor at runtime and must not be added to source control or bundled with the processor test package.

```powershell
dotnet test AppleTVCrestronDriver.Tests/AppleTVCrestronDriver.Tests.csproj --filter "TestCategory!=Processor"
dotnet test AppleTVCrestronDriver.Lifecycle.Tests/AppleTVCrestronDriver.Lifecycle.Tests.csproj
```

Desktop success does not establish Mono compatibility. Build the processor test project in Visual Studio, deploy it, and run both suites on the processor. The fixtures cover configuration, restoration, refresh/reconnect races and disposal using simulated responses. Real installed-driver health and optional live-device checks remain separate from these repeatable suites.


### Driver build and release versions

The driver's JSON manifest is the source of its four-component build version. Debug builds increment only the fourth component; for example, `2.0.001.0005` becomes `2.0.001.0006`. MSBuild's `Version` and default `PackageVersion` are derived from that same manifest and refreshed after the increment; their numeric form is `2.0.1.6`. Assembly binding versions remain separate. Test-only references and IDE design-time builds do not increment the production driver version.

GitHub tags and NuGet releases retain three components: `v2.0.1` and `2.0.1`. Prepare the manifest's first three components for the intended release before tagging. Release CI checks that the tag matches, resets the fourth component to zero, and verifies the generated `.pkg` version against the manifest and release version before publishing. It does not increment the selected patch again. Local Release builds preserve the manifest. A later Debug build can legitimately be newer than a published release; the processor test package has its own independent version.

Deployment validation compares the exact built `.pkg` against the imported catalogue entry and installed instance, numerically including all four components. Upload/import alone does not activate the new version. Keep the tested package and its hash: rebuilding creates a new artifact that must be validated again.

Run `pwsh -File tools/Test-DriverVersioning.ps1` to check these rules with temporary manifests; this does not change the working driver manifest or deploy anything.

See [versioning details](docs/Versioning.md) for build, release and installed-instance verification rules.

For automated local tests, processor tests and gated driver deployment, see the [Crestron Home NUnit CI development guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md). It covers private configuration, live-test gates, install/update waits, results and optional test-package removal.

## Visual Studio processor workflow

The solution includes [AppleTVCrestronDriver.WorkflowTests](AppleTVCrestronDriver.WorkflowTests/README.md), using the published Crestron Home Test Adapter. It exposes the complete gated workflow in Test Explorer while the ordinary NUnit fixtures remain available for local testing. Configure its private settings before execution; hosted CI verifies discovery without accessing hardware.

## Publishing when local hardware is unavailable

The publish/release workflows support an explicit manual override when the processor or local self-hosted GitHub Actions runner is unavailable. Select `skip_hardware_checks` and provide a single-line `hardware_skip_reason`. Use the workflow's normal source and version controls. The override applies only to that invocation and is recorded with the exact source revision in its warning and job summary; it does not create a passing hardware-test result.

GitHub-hosted validation remains mandatory for the checked-out source, and the normal build, tests and packaging steps still run. Wait for the configured hosted workflows to pass, or run them on the same source revision first. None of these hosted checks needs the local runner or processor. Automatic tag/release-triggered runs retain the normal hardware checks; use a manual invocation of the updated release workflow when an offline override is needed.