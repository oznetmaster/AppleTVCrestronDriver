param(
[Parameter(Mandatory)][string] $PkgFile,
[Parameter(Mandatory)][string] $ProcessorIP,
[Parameter(Mandatory)][string] $User,
[Parameter(Mandatory)][string] $Password,
[switch] $Clean
)

Import-Module Posh-SSH -ErrorAction Stop

$cred           = [System.Management.Automation.PSCredential]::new($User, (ConvertTo-SecureString $Password -AsPlainText -Force))
$importPath     = "/user/ThirdPartyDrivers/Import"
$usedPath       = "/user/Data/UsedThirdPartyDrivers"
$deviceManifest = "/user/Data/PyngDeviceManifest/DeviceManifest.cfg"
$localManifest  = Join-Path $env:TEMP 'DeviceManifest.cfg'

$driverManifestPath = Join-Path $PSScriptRoot 'AppleTVCrestronPlatformProof.json'
$driverManifest     = Get-Content $driverManifestPath -Raw | ConvertFrom-Json
$manufacturer       = ($driverManifest.GeneralInformation.Manufacturer -replace '\s', '').ToLowerInvariant()
$model              = ($driverManifest.GeneralInformation.BaseModel -replace '\s', '').ToLowerInvariant()
$company            = ($driverManifest.GeneralInformation.Developer.Company -replace '\s', '').ToLowerInvariant()
$newVersion         = $driverManifest.GeneralInformation.DriverVersion
$driverGuidBase     = "$manufacturer.$model"
$usedFolderPattern  = "$manufacturer.$model*$company"

Write-Host "Connecting to $ProcessorIP..."
$sftpSession = New-SFTPSession -ComputerName $ProcessorIP -Credential $cred -Force -ErrorAction Stop

function Remove-SFTPDirectory {
param([int] $SessionId, [string] $Path)

$children = Get-SFTPChildItem -SessionId $SessionId -Path $Path -ErrorAction SilentlyContinue
foreach ($child in $children) {
$childPath = "$Path/$($child.Name)"
if ($child.IsDirectory) {
Remove-SFTPDirectory -SessionId $SessionId -Path $childPath
}
else {
Write-Host "  Deleting file : $childPath"
Remove-SFTPItem -SessionId $SessionId -Path $childPath -Force -ErrorAction SilentlyContinue
}
}

Write-Host "  Deleting dir  : $Path"
Remove-SFTPItem -SessionId $SessionId -Path $Path -Force -ErrorAction SilentlyContinue
}

try {
if ($Clean) {
Write-Host "Scanning $usedPath for entries matching '$usedFolderPattern'..."
$stale = Get-SFTPChildItem -SessionId $sftpSession.SessionId -Path $usedPath -ErrorAction SilentlyContinue |
Where-Object { $_.Name -like $usedFolderPattern }
if ($stale) {
foreach ($dir in $stale) {
Write-Host "Removing stale entry: $($dir.Name)"
Remove-SFTPDirectory -SessionId $sftpSession.SessionId -Path "$usedPath/$($dir.Name)"
}
}
}

Write-Host "Patching DeviceManifest.cfg (new version: $newVersion)..."
Get-SFTPItem -SessionId $sftpSession.SessionId -Path $deviceManifest -Destination $env:TEMP -Force -ErrorAction Stop

$cfgRaw = Get-Content $localManifest -Raw
$cfgRaw = $cfgRaw -replace "(?<=\`"DriverPath\`":\`"$driverGuidBase[^/]+/)[^/]+(?=/)", $newVersion
$cfgRaw = $cfgRaw -replace "(?<=\`"DriverFolderPath\`":\`"$driverGuidBase[^/]+/)[^\`"]+", $newVersion
$cfgRaw = $cfgRaw -replace "(?<=\`"DriverVersion\`":\`")[^\`"]+(?=\`",\`"DriverGuid\`":\`"$driverGuidBase)", $newVersion

Set-Content -Path $localManifest -Value $cfgRaw -NoNewline -Encoding UTF8
Set-SFTPItem -SessionId $sftpSession.SessionId -Path $localManifest -Destination ($deviceManifest.Substring(0, $deviceManifest.LastIndexOf('/') )) -Force -ErrorAction Stop
Write-Host 'DeviceManifest.cfg patched and uploaded.'

Write-Host "Uploading $(Split-Path $PkgFile -Leaf) to $importPath ..."
Set-SFTPItem -SessionId $sftpSession.SessionId -Path $PkgFile -Destination $importPath -Force -ErrorAction Stop
Write-Host 'Deploy complete.'
}
finally {
if ($sftpSession) {
Remove-SFTPSession -SessionId $sftpSession.SessionId | Out-Null
}
}
