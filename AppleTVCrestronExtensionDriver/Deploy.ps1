param(
[Parameter(Mandatory)][string] $PkgFile,
[Parameter(Mandatory)][string] $ProcessorIP,
[Parameter(Mandatory)][string] $User,
[Parameter(Mandatory)][string] $Password
)

Import-Module Posh-SSH -ErrorAction Stop

$cred           = [System.Management.Automation.PSCredential]::new($User, (ConvertTo-SecureString $Password -AsPlainText -Force))
$importPath     = "/user/ThirdPartyDrivers/Import"
$manifestPath   = Join-Path $PSScriptRoot 'AppleTVCrestronExtensionDriver.json'
$manifest       = Get-Content $manifestPath -Raw | ConvertFrom-Json
$driverVersion  = $manifest.GeneralInformation.DriverVersion

Write-Host "Connecting to $ProcessorIP..."
$sftpSession = New-SFTPSession -ComputerName $ProcessorIP -Credential $cred -Force -ErrorAction Stop

try {
Write-Host "Uploading $(Split-Path $PkgFile -Leaf) (version $driverVersion) to $importPath ..."
Set-SFTPItem -SessionId $sftpSession.SessionId -Path $PkgFile -Destination $importPath -Force -ErrorAction Stop
Write-Host "Upload complete for version $driverVersion. Crestron Home will import and upgrade the package normally."
}
finally {
if ($sftpSession) {
Remove-SFTPSession -SessionId $sftpSession.SessionId | Out-Null
}
}
