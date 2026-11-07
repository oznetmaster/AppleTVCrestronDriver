# AppleTVCrestronExtensionDriver

Version **1.5.0** adds one programmable **Launch <app name>** command per installed app, with wake confirmation built into app launching. It is versioned with the paired Video Server driver.

A **Crestron Home Entity V2 Extension driver** that adds Apple TV app selection and remote-control UI (arrow keys, D-pad, Home/Menu, play/pause/rewind/fast-forward, previous/next track, volume, and on-screen keyboard text entry) to a room, driven over a loopback bridge to the [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver.

> **Trademark notice and disclaimer:** Apple, Apple TV, and tvOS are trademarks of Apple Inc., registered in the U.S. and other countries. This project is an independent, unofficial driver and is **not affiliated with, endorsed by, sponsored by, or approved by Apple Inc.** in any way. "Apple TV" and other Apple product names are used solely to describe compatibility and interoperability. No Apple software, assets, or confidential documentation are included in or derived for this repository. Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc.

[![License: MIT + Commons Clause](https://img.shields.io/badge/License-MIT%20%2B%20Commons%20Clause-blue.svg)](../LICENSE)

---

## Relationship to the Video Server driver

This is an **Extension driver**, not a standalone driver. It requires the [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) Video Server driver to already be configured and paired with the same Apple TV in the room. The extension driver never talks to the Apple TV or Companion Link directly; instead it connects to the Video Server driver's loopback bridge to relay commands (app selection, remote input, volume, keyboard text) and receive status/events (connection state, app list, power state, on-screen keyboard focus).

Because the two drivers are released together and must stay compatible, both are versioned and released from the same [AppleTVCrestronDriver](https://github.com/oznetmaster/AppleTVCrestronDriver) repository under the same release tag. See that repository's root [README](https://github.com/oznetmaster/AppleTVCrestronDriver#readme) for the Video Server driver's own installation, pairing, and testing documentation.

> **Not a video-routing device:** This extension driver has no video inputs or outputs of its own and cannot be selected or routed to a display/switcher — only the Video Server driver can be. Before using this extension driver's remote control UI, the Apple TV must already be the active/selected video source in the room (i.e. the Video Server driver's video routing, as described in its own README's [Pairing](https://github.com/oznetmaster/AppleTVCrestronDriver#pairing) section, must already be selected). This extension driver only adds a control surface on top of whatever Apple TV source is already selected; it does not select or switch video sources itself.

---

## Features

- App selection from a live list of installed Apple TV apps, with the last accepted launch selection shown as the list label
- A separate parameterless **Launch <app name>** command for each installed app, available to Quick Actions and other Home programming
- App launches wake a sleeping Apple TV and wait for confirmed awake state before sending the launch request; an already-on Apple TV launches without an extra Home command or startup wait
- Full remote control: D-pad arrow navigation, Select, Menu, Home (which also resets the app selector back to its default label)
- Discrete power toggle with status label
- Play/Pause, Rewind, Fast Forward, and separate Previous/Next track (skip) buttons
- Volume up/down and mute, shown only when the paired Apple TV/session reports volume control support
- On-screen keyboard text entry: automatically appears when the Apple TV requests text input (e.g. Spotlight search, sign-in) and forwards each keystroke live, matching the behavior of Apple's own Remote app; disappears automatically when the Apple TV dismisses the text field
- Automatically reflects the Video Server driver's connection status, reconnecting its bridge session if the connection drops

---

## Installation

Update this extension and its Video Server driver from the same release. The Entity V2 extension supports an in-place update. Updating the V1 Video Server requires a full processor reboot. Keep the existing paired Video Server device and its saved configuration during the update.

Allow the Apple TV to finish sleeping before turning it back on. An immediate wake during a sleep transition was ignored in live testing; a five-second settling pause worked. This does not establish a minimum delay.

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

> **Handheld remotes:** The extension UI does not render as a native TSR-310 source screen. Its app commands can still be used in Quick Actions. Keep the Video Server for the native source and remote controls; this release does not add title/artwork to the TSR-310 Now Playing box.

---

## Quick Actions

Use one Quick Action per app you want to offer, such as **Watch BBC iPlayer** or **Watch Disney+**. For the user this is one button. Its installer configuration contains two sequence steps because Home controls room routing while the extension controls app launching.

### Crestron Home Setup

1. Verify the paired **Apple TV Companion** Video Server has an HDMI route to the room's display or switcher. Verify **Apple TV Companion Media** is online and its app selector has populated.
2. Open **Customize & Schedule → Customize Actions & Events**, select the target room, choose **Quick Actions** and add a Quick Action, for example **Watch BBC iPlayer**.
3. On its **Actions** tab, choose **Sequence** mode.
4. Add the first step from the target room's **Media Operations → Route**. Select the Video Server's **AppleTV** source (the device name may differ in your installation).
5. Add the second step from **Devices → Apple TV Companion Media** (or its configured name). Select **Launch BBC iPlayer**. This command has no parameter.
6. Confirm the order is **Route AppleTV → Launch BBC iPlayer**, save the Quick Action and choose the rooms in which its button should be visible.

### Configure Pro

1. Add a Quick Action, assign it to the target room and set **Mode → Sequence**.
2. Add **Media Operations → Route**, selecting the Video Server's Apple TV source.
3. Add the named extension under **Devices**, then its **Launch <app name>** command.
4. Keep Route first and Launch second. Set **Visible in Rooms** and save. Menu labels can vary slightly between app versions.

Do not add a separate Home/wake command. The extension sends Home only when it needs to wake Apple TV, waits up to 60 seconds for confirmed awake state, then sends the app launch. It does not wait the full minute when confirmation arrives sooner. Home manages display power, routing and its configured transition times. Keep those settings appropriate for the installed equipment.

Test a new action with the TV/room off, then check both the correct TV input and the requested app. Also try switching between actions for different apps. The live normal-use test used an Apple TV configured to remain on: the TV was confirmed off, the two-step action turned it on and displayed BBC iPlayer after a previous Disney+ selection. Sleeping-Apple-TV wake ordering has automated coverage; this does not claim that every tvOS/display timing combination was tested live.

### Installed-app changes and feedback

Commands appear when the extension receives the app list and update as that list is refreshed. Their IDs derive from the complete app bundle ID, so list reordering or display-name changes do not redirect saved programming to a different app. A removed app is unavailable; reinstall the same app and verify the saved Quick Action. Unavailable command definitions are retained during the current driver session; an app still absent after a driver restart may no longer be offered in programming.

The app-selection label records an accepted launch request, not independently confirmed foreground-app or movie metadata. An offline connection, unavailable app, failed wake confirmation, configuration change or disconnect prevents the pending launch. Concurrent app requests are serialized.

Keep the V1 source available for normal source selection. Deselecting it under **Allowed Sources** hides it from the source picker; that does not provide a way to suppress only its control UI while retaining selection. Neither driver's visibility is changed by this release. The extension does not replace the V1 device's pairing, native source routing or TSR-310 controls.

See Crestron's [Setup Quick Actions guide](https://docs.crestron.com/en-us/8525/Content/CP4R/Installer-Config/Configure-Quick-Actions.htm) and [Configure Pro Quick Actions guide](https://docs.crestron.com/en-us/9599/Content/Quick%20Actions.htm).

---

## Building from Source

### Dependencies

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

## Tests

The shared [NUnit suite](../AppleTVCrestronDriver.Tests) includes **17 dedicated extension-driver
logic tests** covering bridge feedback parsing, app-list sorting, selection preservation and
clearing stale selections. Bridge and keyboard tests provide additional shared coverage. The suite also includes 19 app-command and launch-ordering cases. These
are part of the 149 offline tests run by CI and by the [processor test package](../AppleTVCrestronDriver.ProcessorTests/README.md).

Run the `AppleTvExtensionDriverLogicTests` fixture in Visual Studio Test Explorer or discover
and select it in the Windows NUnit runner. The same processor package tests both drivers and
provides its own Utility tile. The separate real-SDK lifecycle suite covers 15 construction, configuration, dynamic command registration, bridge-feedback and disposal cases. UI rendering is checked on the installed driver.

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
See [test dependencies and validation instructions](../AppleTVCrestronDriver.Tests/README.md) for both drivers.
