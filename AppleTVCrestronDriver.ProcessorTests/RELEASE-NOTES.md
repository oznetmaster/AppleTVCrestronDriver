# AppleTVCrestronDriver Tests

## 1.0.0 — 2026-09-14

- 105 offline tests and 11 SDK lifecycle tests, shared between desktop validation and the net472 processor package.
- Cover saved pairing, shared credential restoration, discovery identity persistence, configured-name changes, retry recovery and superseded discovery/connection attempts. Cancellation prevents old connection attempts from persisting identity or reporting a paired state for a newer configuration.
- Install the standalone test package from Configure’s **Utility** category. Select suites using its Home tile or the Windows NUnit runner.
- Processor test packages are GitHub release assets and are not published to NuGet. Private test inputs and deployment settings are excluded.
