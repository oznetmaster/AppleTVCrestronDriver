# Copyright (c) 2026 Neil Colvin. MIT with Commons Clause; see LICENSE.
# Run with Windows PowerShell to exercise the final patched net472 assembly.
param([Parameter(Mandatory)][string[]] $AssemblyPath, [string] $DependencyDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.Serialization
foreach ($path in $AssemblyPath) {
    $resolvedPath = (Resolve-Path -LiteralPath $path).Path
    $dependencies = $DependencyDirectory
    if (-not $dependencies) {
        $dependencies = Split-Path -Parent $resolvedPath
        if ((Split-Path -Leaf $dependencies) -eq 'patched') { $dependencies = Split-Path -Parent $dependencies }
    }
    # Loading the SDK assemblies from their output directory lets the framework resolve
    # SDK-owned types while examining the patched assembly's data contracts.
    foreach ($name in @('RADCommon.dll', 'Crestron.DeviceDrivers.EntityModel.dll', 'Crestron.DeviceDrivers.SDK.dll')) {
        $dependency = Join-Path $dependencies $name
        if (Test-Path -LiteralPath $dependency) { [Reflection.Assembly]::LoadFrom($dependency) | Out-Null }
    }
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $path).Path)
    if ($assembly.GetReferencedAssemblies().Name -match '^(Newtonsoft\.Json|log4net)') {
        throw 'Unexpected application JSON/logging dependency.'
    }
    $type = $assembly.GetType('AppleTV.CrestronDriver.AppleTvStoredDevice+StoredDeviceFile', $true)
    $serializer = [Runtime.Serialization.Json.DataContractJsonSerializer]::new($type)
    $json = '{"Name":"Saved TV","UniqueId":"saved-id","StableIdentifier":"stable","Port":4321,"Ltpk":"AQI=","Ltsk":"AwQ=","AtvId":"BQ==","ClientId":"Bg==","FutureField":true}'
    $input = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($json))
    try { $file = $serializer.ReadObject($input) } finally { $input.Dispose() }
    $method = $type.GetMethod('ToStoredDevice', [Reflection.BindingFlags]'Instance,NonPublic')
    $device = $method.Invoke($file, @())
    if ($device.Name -ne 'Saved TV' -or $device.UniqueId -ne 'saved-id' -or $device.StableIdentifier -ne 'stable' -or $device.Port -ne 4321 -or -not $device.IsPaired) {
        throw 'Existing pairing metadata did not survive deserialization.'
    }
    if ([Convert]::ToBase64String($device.Ltpk) -ne 'AQI=' -or [Convert]::ToBase64String($device.Ltsk) -ne 'AwQ=') {
        throw 'Pairing key bytes changed.'
    }
    $output = [IO.MemoryStream]::new()
    try {
        $serializer.WriteObject($output, $file)
        $saved = [Text.Encoding]::UTF8.GetString($output.ToArray()) | ConvertFrom-Json
        if ($saved.UniqueId -ne 'saved-id' -or $saved.StableIdentifier -ne 'stable' -or $saved.Ltpk -ne 'AQI=') {
            throw 'Saved field names or key encoding changed.'
        }
    } finally { $output.Dispose() }
    Write-Output "$($assembly.GetName().Name): final merged storage contract passed."
}
