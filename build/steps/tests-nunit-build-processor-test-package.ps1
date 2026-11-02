msbuild AppleTVCrestronDriver.ProcessorTests/AppleTVCrestronDriver.ProcessorTests.csproj /restore /nologo /v:minimal /p:Configuration=Release /p:BuildProcessorTestPackages=true /p:ReleaseVersion=1.1.0 /p:DeployAfterBuild=false /p:MergeDependenciesContinueOnError=false "/p:ManifestUtilExe=${env:BUILD_INPUT_0}"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
