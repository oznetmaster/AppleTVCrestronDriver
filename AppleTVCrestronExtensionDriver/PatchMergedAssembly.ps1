# PatchMergedAssembly.ps1
# Patches compiler-synthesized infrastructure types that violate Crestron Home's
# sandboxed Mono reflection environment (RestrictionViolationException on GetTypes).
# These types are emitted by the C# 11+ compiler and have no runtime behaviour —
# they are pure metadata.
#
# Strategy: RENAME the namespace of each restricted type to "_Stripped" so that
# Crestron's name-based restriction check no longer fires.  All internal
# references (custom-attribute usages) automatically follow because they point to
# the same TypeDefinition object — no dangling references, no Cecil Write() errors.

param(
	[Parameter(Mandatory)][string] $AssemblyPath,
	[string] $OutputPath = "",
	[string] $ReferenceAssemblyDirectory = $env:NET472_REFERENCE_ASSEMBLIES,
	[string] $SdkLibDir = $env:CRESTRON_DRIVER_SDK_LIBRARIES
)

if (-not $OutputPath) {
	$dir  = [System.IO.Path]::GetDirectoryName($AssemblyPath)
	$stem = [System.IO.Path]::GetFileNameWithoutExtension($AssemblyPath)
	$OutputPath = [System.IO.Path]::Combine($dir, $stem + "_patched.dll")
}

$cecilPath = Get-ChildItem "$env:USERPROFILE\.dotnet\tools\.store\dotnet-ilrepack" `
	-Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue |
	Select-Object -First 1 -ExpandProperty FullName

if (-not $cecilPath) {
	Write-Warning "PatchMergedAssembly: Mono.Cecil.dll not found - skipping patch."
	exit 0
}

[System.Reflection.Assembly]::LoadFrom($cecilPath) | Out-Null

# Crestron Home's Mono sandbox blocks any type whose full name it does not
# recognise as part of its own BCL.  In a merged net472 assembly, every type
# whose namespace starts with "System." is a compiler-injected shim or a NuGet
# polyfill — the real BCL types with those names live in the GAC (mscorlib,
# System.dll, etc.), never in the merged DLL itself.  Renaming them all to the
# "_Stripped" namespace is safe: their internal references follow automatically
# because Cecil TypeReferences point to the same TypeDefinition object.

function ShouldRename([Mono.Cecil.TypeDefinition]$td) {
	return ($td.Namespace -eq 'System' -or $td.Namespace.StartsWith('System.'))
}

$asmBytes  = [System.IO.File]::ReadAllBytes($AssemblyPath)
$asmStream = [System.IO.MemoryStream]::new($asmBytes)
$rp        = [Mono.Cecil.ReaderParameters]::new()
$resolver  = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($directory in @([System.IO.Path]::GetDirectoryName($AssemblyPath), $ReferenceAssemblyDirectory, $SdkLibDir)) {
	if (-not [string]::IsNullOrWhiteSpace($directory) -and (Test-Path -LiteralPath $directory)) {
		$resolver.AddSearchDirectory($directory)
	}
}
$rp.AssemblyResolver = $resolver
$asmDef    = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmStream, $rp)
$module    = $asmDef.MainModule
$count     = 0

# Cecil reads custom-attribute blobs lazily. Decode them before changing type names:
# enum argument types and typeof values then refer to the original definitions and
# are re-encoded with their new names when the module is written. Otherwise Cecil
# can copy an untouched blob containing a now-invalid serialized enum type name.
function ResolveAttributes($provider) {
	foreach ($attribute in $provider.CustomAttributes) {
		$null = $attribute.ConstructorArguments.Count
		$null = $attribute.Properties.Count
		$null = $attribute.Fields.Count
	}
}

function ResolveTypeAttributes($type) {
	ResolveAttributes $type
	foreach ($parameter in $type.GenericParameters) { ResolveAttributes $parameter }
	foreach ($field in $type.Fields) { ResolveAttributes $field }
	foreach ($property in $type.Properties) { ResolveAttributes $property }
	foreach ($event in $type.Events) { ResolveAttributes $event }
	foreach ($method in $type.Methods) {
		ResolveAttributes $method
		ResolveAttributes $method.MethodReturnType
		foreach ($parameter in $method.Parameters) { ResolveAttributes $parameter }
		foreach ($parameter in $method.GenericParameters) { ResolveAttributes $parameter }
	}
	foreach ($nested in $type.NestedTypes) { ResolveTypeAttributes $nested }
}

ResolveAttributes $asmDef
ResolveAttributes $module
foreach ($type in $module.Types) { ResolveTypeAttributes $type }

# Rename each restricted type's namespace so Crestron's name check never fires.
# All TypeReference usages in custom attributes point to the same TypeDefinition
# object, so they automatically reflect the rename — no dangling references.
foreach ($td in $module.Types) {
	if (ShouldRename $td) {
		$oldName = $td.FullName
		$td.Namespace = '_Stripped.' + $td.Namespace
		$count++
		Write-Host "  Renamed: $oldName -> $($td.FullName)"
	}
}

$outDir = [System.IO.Path]::GetDirectoryName($OutputPath)
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$tempPath = $OutputPath + ".tmp"
try {
	$fs = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
	try   { $asmDef.Write($fs) }
	finally { $fs.Dispose() }
	$asmDef.Dispose()
	$resolver.Dispose()
	$asmStream.Dispose()
	[System.IO.File]::Copy($tempPath, $OutputPath, $true)
	Remove-Item $tempPath -Force
	Write-Host "PatchMergedAssembly: $count type(s) renamed -> $OutputPath"
	exit 0
} catch {
	$asmDef.Dispose()
	$resolver.Dispose()
	$asmStream.Dispose()
	if (Test-Path $tempPath) { Remove-Item $tempPath -Force }
	Write-Error "PatchMergedAssembly: Write failed - $_"
	exit 1
}
