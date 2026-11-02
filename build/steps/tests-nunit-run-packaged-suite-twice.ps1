$output = Join-Path $PWD 'AppleTVCrestronDriver.ProcessorTests/bin/Release/net472'
$extracted = Join-Path $env:RUNNER_TEMP 'appletv-package'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $output 'AppleTVCrestronDriver.ProcessorTests.pkg'), $extracted)
$assembly = Get-ChildItem $extracted -Recurse -Filter AppleTVCrestronDriver.ProcessorTests.dll | Select-Object -First 1
./tools/Test-DiscoveredCoverage.ps1 -Stage Package -SdkRoot ../CrestronHomeNUnit -PackageAssembly $assembly.FullName -SourceInventory TestResults/AppleTVCrestronDriver.Tests/inventory.json -SuiteCategories @{'appletv-driver'='unit';'appletv-extension-lifecycle'='processor'} -ValidatorPath (Join-Path $output 'validation-host/ProcessorTestPackage.Validation.exe') -ResultsDirectory TestResults
