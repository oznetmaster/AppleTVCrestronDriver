param(
	 [Parameter(Mandatory)][string] $AssemblyPath,
	 [Parameter(Mandatory)][string] $OutputPath
)

$cecilPath = Get-ChildItem "$env:USERPROFILE\.dotnet\tools\.store\dotnet-ilrepack" -Recurse -Filter 'Mono.Cecil.dll' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $cecilPath) {
	 throw 'Mono.Cecil.dll not found.'
}

[System.Reflection.Assembly]::LoadFrom($cecilPath) | Out-Null
$bytes = [System.IO.File]::ReadAllBytes($AssemblyPath)
$stream = [System.IO.MemoryStream]::new($bytes)
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($stream)

foreach ($type in $assembly.MainModule.Types) {
	 if ($type.Namespace -eq 'System' -or $type.Namespace.StartsWith('System.')) {
		  $type.Namespace = '_Stripped.' + $type.Namespace
	 }
}

$assembly.Write($OutputPath)
$assembly.Dispose()
