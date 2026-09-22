# Apple TV drivers 1.4.4

Patch release of the video driver and companion extension. Updates stable SDK/compatibility dependencies and the video driver's AppleTvControlLibrary dependency to 2.2.6. Existing pairing files, configuration and driver interfaces remain compatible. No re-pairing is required by this update.

Credential field names are explicitly controlled by data-contract attributes. Merged runtime patching preserves custom-attribute metadata. Neither driver adds Newtonsoft.Json or log4net application dependencies.

Validation covers 108 net472 offline tests, 11 .NET 10 extension lifecycle tests, and the same 119 cases in a temporary processor test host. Neither actual driver is deployed for this validation and no processor reboot is performed. Real-driver installation and Apple TV control are not claimed as tested by these simulated suites.

See [test dependencies and instructions](AppleTVCrestronDriver.Tests/README.md).
