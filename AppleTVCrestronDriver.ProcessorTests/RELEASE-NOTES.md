# AppleTVCrestronDriver Tests

## 1.0.2

- Classify the 11 simulated extension lifecycle tests as an automatic processor suite, alongside the 105 unit/integration cases. A test-only workflow can now execute all 116 cases without live-test opt-in.
- The lifecycle tests construct isolated test entities and supply synthetic bridge events. They do not connect to a physical Apple TV or configure the installed production driver.
- This updates only the GitHub processor test package. No production driver or NuGet release is included.

## 1.0.1

- Rebuild with CrestronHomeNUnit 1.2.1. Test execution now participates in the shared processor reservation used by the runner, Test Explorer, CLI and hardware CI.
- The net472 package contains 116 discovered cases, with 105 in automatic suites. Live suites remain optional and require private inputs where documented.
- Use the standalone Utility tile, Windows runner, or the solution's Test Explorer workflow project. Private workflow plans can remove the temporary instance after testing.
- This is an independent processor-test package release on GitHub; it does not publish or update a driver/library NuGet package.

## 1.0.0 — 2026-09-14

- 105 offline tests and 11 SDK lifecycle tests, shared between desktop validation and the net472 processor package.
- Cover saved pairing, shared credential restoration, discovery identity persistence, configured-name changes, retry recovery and superseded discovery/connection attempts. Cancellation prevents old connection attempts from persisting identity or reporting a paired state for a newer configuration.
- Install the standalone test package from Configure’s **Utility** category. Select suites using its Home tile or the Windows NUnit runner.
- Processor test packages are GitHub release assets and are not published to NuGet. Private test inputs and deployment settings are excluded.
