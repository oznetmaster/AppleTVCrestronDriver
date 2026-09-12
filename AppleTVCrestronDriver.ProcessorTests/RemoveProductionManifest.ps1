# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License with Commons Clause; see LICENSE in the repository root.
param(
    [Parameter(Mandatory)][string] $AssemblyPath,
    [Parameter(Mandatory)][string] $CecilPath
)
$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.InMemory = $true
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath, $parameters)
try {
    foreach ($resource in @($assembly.MainModule.Resources)) {
        if ($resource -isnot [Mono.Cecil.EmbeddedResource] -or -not $resource.Name.EndsWith('.json')) { continue }
        $json = [Text.Encoding]::UTF8.GetString($resource.GetResourceData()).TrimStart([char]0xFEFF)
        try { $document = ConvertFrom-Json -InputObject $json -ErrorAction Stop }
        catch { continue }
        if ($document.CrestronSerialDeviceApi) {
            $assembly.MainModule.Resources.Remove($resource) | Out-Null
            Write-Output "Excluded legacy production driver manifest: $($resource.Name)"
        }
    }
    $assembly.Write($AssemblyPath)
}
finally { $assembly.Dispose() }
