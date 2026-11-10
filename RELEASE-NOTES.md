# AppleTVCrestronDriver 1.5.1

This patch corrects the dependency and lifecycle issues described below.

- Use AppleTVControlLibrary and Discovery 2.2.8.
- Keep queued Video Server commands tied to the session that accepted them. Drop retired commands and drain held-key release and session shutdown before replacement.
- Preserve the existing V1/V2 bridge, pairing files, source routing and Quick Action configuration.

## Installation

Use the driver package from [GitHub](https://github.com/oznetmaster/AppleTVCrestronDriver/releases/tag/v1.5.1) or the matching NuGet distribution. Import the package, then update the existing driver in Home Setup or Configure Pro. Importing alone does not update the running instance. Existing pairing and configuration formats are retained; removal and re-add is not a routine update step.

Both the V1 Video Server and V2 companion are released together as 1.5.1. Updating an existing V1 driver requires a full processor reboot; the V2 companion does not require a reboot. Preserve the existing pairing files. Quick Actions keep the two-step Route AppleTV -> Launch app setup.

## Validation

Passed 167 workstation Debug and 167 Release test executions, plus 167 native net472/Mono processor tests. Processor validation used fake transports and synthetic credentials; no new physical-device acceptance or endurance run is claimed. Existing processor configuration and all pairing files were preserved. See [development history](DEVELOPMENT-HISTORY.md) for provenance and limits.

This GitHub/NuGet release does not replace any submitted Crestron package. Submission-specific PDFs and correspondence are excluded from public artifacts.
