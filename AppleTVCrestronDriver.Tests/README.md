# Testing both Apple TV drivers

| Dependency | Version | Purpose |
| --- | --- | --- |
| NUnit | 4.6.1 | Test framework |
| NUnit3TestAdapter | 6.3.0 | Desktop discovery and execution |
| Microsoft.NET.Test.Sdk | 18.10.1 | Desktop test host |
| Crestron.DeviceDrivers.DevKit | 29.0.10 | SDK used by both drivers and lifecycle fixtures |
| Crestron.SimplSharp.SDK.Library | 2.22.15 | SIMPL# runtime references |
| Microsoft.Bcl.Memory | 10.0.12 | Compatibility support |
| CrestronHomeNUnit.TestAdapter | 1.12.1 | Separate opt-in processor workflow project |

The main suite runs 108 offline tests on net472. The desktop lifecycle harness runs 11 extension lifecycle tests on .NET 10. The separate processor package runs the same 119 cases on Mono, including both drivers' logic. Pairing tests use simulated Apple TVs; no production pairing credentials are needed.

```powershell
dotnet test AppleTVCrestronDriver.Tests/AppleTVCrestronDriver.Tests.csproj -c Release -p:DeployAfterBuild=false -p:SkipDriverPackaging=true --filter "TestCategory!=Processor&TestCategory!=Live"
dotnet test AppleTVCrestronDriver.Lifecycle.Tests/AppleTVCrestronDriver.Lifecycle.Tests.csproj -c Release --filter "TestCategory=Processor&TestCategory!=Live"
```

The driver checkout and AppleTVControlLibrary checkout must be siblings so the pairing fixtures can use the library's simulated device. Install the .NET 10 SDK, net472 developer pack and the build tools described in the root README. The SDK lifecycle tests additionally need a private, legally obtained Newtonsoft.Json.Compact.dll in the excluded repository root. It satisfies a Crestron SDK assembly dependency; it is not application JSON code and is not merged into either production package. The ordinary driver/library source uses neither Newtonsoft nor log4net.

Stored credentials use the framework DataContractJsonSerializer with explicitly named DataMember attributes. This preserves existing base64 pairing files without introducing an extra runtime serializer package. Regressions cover legacy files with unknown fields, discovery-only records and corrupt base64 alongside valid records. Driver logging continues through the native Crestron SDK logging surfaces.

All dependency updates use stable versions. Keep plist-cil 2.2.0, inherited from the control library: 2.3.1 does not support net472. SDK-owned and desktop test-tool transitive dependencies follow their parent packages. Desktop adapter/telemetry assemblies are excluded from the processor package merge.

See [processor tests](../AppleTVCrestronDriver.ProcessorTests/README.md). These run in a temporary Utility test host. They do not require deployment of either actual driver or a processor reboot, and do not establish real-driver installation or end-to-end hardware control coverage.

The test-only FakeDevice project compiles the shared simulated-device source against the same published NuGet library as the driver. This prevents source/package assembly-version conflicts in processor builds. It is not shipped with either production driver.
