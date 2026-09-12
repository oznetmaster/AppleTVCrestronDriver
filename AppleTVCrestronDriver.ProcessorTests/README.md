# AppleTVCrestronDriver processor tests

Runs the same 97 NUnit unit/integration tests as the Windows test project on a Crestron Home processor. The tests cover both Apple TV drivers, pairing against a simulated Companion device, stored-device persistence, bridge communication and connection/reconnect logic. They do not operate a physical Apple TV.

## Build and deploy

1. Keep `AppleTVCrestronDriver`, `AppleTVControlLibrary` and `CrestronHomeNUnit` as sibling checkouts. Install the Crestron Driver SDK and .NET Framework 4.7.2 development tools.
2. Open `AppleTVCrestronDriver.slnx` in Visual Studio.
3. Configure deployment in your private `AppleTVCrestronDriver.ProcessorTests.csproj.user` and any machine-specific paths in `AppleTVCrestronDriver.ProcessorTests.Local.targets`. Keep both excluded through `.git/info/exclude`; never commit credentials or private paths.
4. Build **AppleTVCrestronDriver.ProcessorTests** in **Debug**. Its private deployment configuration enables deployment from Visual Studio. Production driver dependencies are built without packaging or deployment.
5. In Crestron Home Configure, add **AppleTVCrestronDriver Tests** from **Utility**.

The package is written to `bin/Debug/net472/AppleTVCrestronDriver.ProcessorTests.pkg`. It targets only `net472` with the shared host's latest C# language setting and compatibility processing. The default SDK path is the sibling checkout; override `ProcessorTestSdkRoot` in the private local targets file if necessary.

For a command-line package build without deployment, use Visual Studio MSBuild:

```powershell
msbuild AppleTVCrestronDriver.ProcessorTests/AppleTVCrestronDriver.ProcessorTests.csproj /restore /p:BuildProcessorTestPackages=true /p:DeployAfterBuild=false
```

## Run tests

The package owns a standalone test tile, distinct from either production driver. Its tile can run the **Unit and Integration Tests** suite and report results without a Windows connection. Alternatively, use **Find packages** in the [Crestron Home NUnit runner](https://github.com/oznetmaster/CrestronHomeNUnit), select this package, connect and run all or discover and select individual tests.

The processor assigns the TCP port; the package advertises it for discovery. No reserved port or separate NUnit host deployment is needed. No `LiveTestSettings.json`, Apple TV credentials or test inputs are required. These simulated tests do not establish production pairing or install the original drivers' UIs.

The build checks that all 97 tests are discoverable after merging. Desktop validation uses a private output copy of Compact JSON, which remains a platform dependency and is not embedded in the test package. All 97 tests have also passed on the processor's Mono runtime. The four pairing/session tests took approximately 10–11 seconds each; their timing has not been profiled.

## Distribution and licenses

This is a development `.pkg`, not a NuGet package. The existing production release workflow does not publish it. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the bundled `Licenses` directory. Driver and fixture source retain the repository's MIT with Commons Clause license; the NUnit host and third-party dependencies retain their own licenses.

Apple, Apple TV, Crestron and Crestron Home are trademarks of their respective owners. This independent project is not affiliated with or endorsed by Apple or Crestron. Crestron SDK components remain subject to Crestron's SDK license agreement.
