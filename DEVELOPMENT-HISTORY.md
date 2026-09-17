# Development and validation history

See the [product changelog](CHANGELOG.md) for shipped changes. This document preserves test, CI, build and submission preparation history. Dated development entries describe work at that time, not a published product version or completed acceptance. Version headings identify the release alongside which development work was recorded; processor-test versions identify separate test packages.

## Where changes belong

- Product changelog and product release notes: shipped behavior, API, compatibility, fixes and runtime dependencies. Mention validation briefly when it helps explain a fix.
- This history: test coverage, CI, build tooling, test-package releases and work on pending candidates. Split mixed entries so the product effect remains easy to find.
- Testing and workflow guides: current setup and operating instructions. Submission guides, where applicable: preparation, evidence and acceptance status.
- Test-only or documentation-only changes do not require a product release. Processor-test releases update this history, not the product changelog.

<!-- development-history -->

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## Discovery-based CI coverage - 2026-09-15 (no package release)

- Compare desktop results and merged-package discovery with source test identities, replacing duplicated test-count constants. Verify the separate desktop lifecycle harness covers the processor fixture identities.
- Validate both packaged executions, allowing only the documented processor-runtime skips on Windows. The desktop SDK harness executes those lifecycle tests; hardware CI executes them on the processor.
- No actual driver code or public API changes. Live device tests remain excluded from hosted execution.

## CI package cleanup - 2026-09-15 (no driver or processor package release)

- Update Test Explorer workflow containers to CrestronHomeNUnit.TestAdapter 1.3.0 and document opt-in storage cleanup after successful CI runs.
- Retain original deployment filenames, protect pre-existing/manual packages and preserve failed-run evidence. Cleanup frees archive storage without rebooting; Home can retain cached catalogue entries until its next planned reboot.
- Actual driver/library code and processor test packages are unchanged by this tooling update.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

## AppleTVCrestronDriver.ProcessorTests v1.0.2 - 2026-09-15

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## Processor test classification - 2026-09-15 (no driver release)

- Make simulated extension lifecycle tests an automatic processor suite so the test-only CI plan can execute all 116 required cases.

## AppleTVCrestronDriver.ProcessorTests v1.0.1 - 2026-09-15

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 2026-09-15 - Test and development tooling (no driver release)

- Add the published Test Explorer workflow adapter, offline discovery CI and independent GitHub processor-test releases. Private workflow plans control optional live tests, actual-driver updates and temporary-instance cleanup.

## 1.4.3 — 2026-09-14

- Prevent superseded discovery and connection attempts from saving device identity or reporting an obsolete paired state. Add saved-pairing, shared-credential, configuration replacement and retry recovery regression tests.

- Standardize driver versioning: Debug project/package metadata follows the manifest including its build increment; local Release builds preserve it; three-part release tags select the exact CI release without another patch increment. Verify source and built package versions before publication.

- Expand driver coverage to 105 offline tests and 11 SDK entity/lifecycle tests, with a desktop SDK harness and the same lifecycle fixtures in the net472 processor package.

- Filename-sanitization test now checks the current runtime's invalid filename characters, rather than assuming Windows restrictions on the processor's Mono runtime. Driver storage behavior is unchanged.

- Converted all 97 public driver unit/integration tests from MSTest to NUnit, preserving per-test fixture instances and serial execution.

- Test builds skip production driver version bumps, packaging and deployment while retaining dependency merging.

- A `net472` processor test project in the existing solution, with a standalone Utility tile and Windows NUnit runner discovery.

- Processor package discovery validation, isolated desktop validation dependencies, license notices and private Visual Studio Debug deployment settings.

## Paired extension driver development

## 1.4.3 — 2026-09-14

### Tests and build process

- Add 11 SDK lifecycle tests shared between desktop validation and the net472 processor package, including configuration cleanup and controller state behavior.

- Align Debug build versions and three-part CI release tags with the driver manifest.

These runtime fixes justify a driver patch; test additions alone do not. See the shared [release notes](RELEASE-NOTES.md).