# Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ManifestSha256,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') { throw 'Use a three-part public release tag.' }
$version = $Matches.version
if ($ManifestSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Provide the reviewed manifest SHA-256.' }
$manifestPath = Join-Path $PackageDirectory 'ReleasePackages.json'
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ine $ManifestSha256) { throw 'Prepared manifest hash differs.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$repo = Split-Path $PSScriptRoot -Parent
if (@(git -C $repo status --porcelain --untracked-files=no).Count -gt 0 -or $LASTEXITCODE -ne 0) { throw 'Use an unchanged release checkout.' }
$commit = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the release checkout.' }
if ($manifest.SchemaVersion -ne 1 -or $manifest.Version -cne $version -or $manifest.SourceCommit -cne $commit) { throw 'Prepared artifacts do not identify this exact release source.' }
$specs = @(
    @{ File='AppleTVCrestronCloudDriver.pkg'; Project='AppleTVCrestronDriver'; Manifest='AppleTVCrestronDriver.json'; Id='CrestronHomeDriver.Apple.AppleTV'; Display='AppleTV Driver'; Category='Video Server' },
    @{ File='AppleTVCrestronExtensionDriver.pkg'; Project='AppleTVCrestronExtensionDriver'; Manifest='AppleTVCrestronExtensionDriver.json'; Id='CrestronHomeDriver.Apple.AppleTVExtension'; Display='AppleTV Extension Driver'; Category='Extension' }
)
if (@($manifest.Packages).Count -ne 2 -or @($manifest.Packages.FileName | Sort-Object -Unique).Count -ne 2) { throw 'Expected exactly two distinct driver packages.' }
foreach ($spec in $specs) {
    $entry = @($manifest.Packages | Where-Object FileName -CEQ $spec.File)
    if ($entry.Count -ne 1 -or $entry[0].Sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Prepared package identity is missing or invalid.' }
    $path = Join-Path $PackageDirectory $spec.File
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $entry[0].Sha256) { throw 'Prepared package bytes differ from the tested artifact.' }
    & "$PSScriptRoot/Assert-DriverPackageVersion.ps1" -PackagePath $path -ManifestPath "$repo/$($spec.Project)/$($spec.Manifest)" -ReleaseVersion $version
}
& "$PSScriptRoot/Assert-PublicDriverArtifacts.ps1" -Path @($specs | ForEach-Object { Join-Path $PackageDirectory $_.File })
if ($ValidateOnly) { Write-Output 'Verified source identity, versions, both tested payloads and public contents.'; return }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
foreach ($spec in $specs) {
    $project = "$repo/$($spec.Project)/$($spec.Project).csproj"
    dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw 'Driver packaging restore failed.' }
    $packageMetadata = Join-Path $OutputDirectory ($spec.Project + '.package.json')
    [ordered]@{
        standardVersion='1.0'; packageType='CrestronHomeDriver'; driverPlatform='Crestron Home'; payloadType='pkg'; payloadFile=$spec.File
        containsManagedDllPayload=$false; packageId=$spec.Id; packageVersion=$version; displayName=$spec.Display; vendor='Apple'; driverCategory=$spec.Category
    } | ConvertTo-Json | Set-Content -LiteralPath $packageMetadata -Encoding utf8
    $payload = Join-Path $PackageDirectory $spec.File
    dotnet pack $project -c Release --no-build --output $OutputDirectory "-p:PackageVersion=$version" "-p:RepositoryCommit=$commit" "-p:PackageDriverPkgPath=$payload" "-p:PackageDriverManifestPath=$packageMetadata" "-p:PackageReleaseNotesFile=$repo/RELEASE-NOTES.md"
    if ($LASTEXITCODE -ne 0) { throw 'Driver NuGet packaging failed.' }
    $nupkg = Join-Path $OutputDirectory "$($spec.Id).$version.nupkg"
    $archive = [IO.Compression.ZipFile]::OpenRead($nupkg)
    try {
        $entries = @($archive.Entries | Where-Object FullName -CEQ $spec.File)
        if ($entries.Count -ne 1) { throw 'NuGet package must contain exactly one expected driver payload.' }
        $stream = $entries[0].Open()
        try { $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        $entry = @($manifest.Packages | Where-Object FileName -CEQ $spec.File)[0]
        if ($sha -ine $entry.Sha256) { throw 'NuGet payload differs from the tested driver package.' }
    } finally { $archive.Dispose() }
}
& "$PSScriptRoot/Assert-PublicDriverArtifacts.ps1" -Path @((Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.nupkg').FullName)
