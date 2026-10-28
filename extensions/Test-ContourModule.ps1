#Requires -Version 7.4

<#
.SYNOPSIS
Checks a WASM artifact against Contour's pinned statusline contract.
.DESCRIPTION
Validates the binary and audits imports, canonical exports, start section and initial memory.
This preflight does not replace execution in Contour or prove runtime memory/deadline behavior.
.PARAMETER Path
Core WASM artifact to inspect.
.PARAMETER WasmToolsPath
Path to wasm-tools supplied by the pinned build package.
.PARAMETER RequireCompatible
Fails if preflight finds an incompatible declaration.
.EXAMPLE
./Test-ContourModule.ps1 -Path ./statusline.wasm -WasmToolsPath ./wasm-tools.exe
.OUTPUTS
System.Management.Automation.PSCustomObject
.NOTES
Contract pinned to Contour 5413eb93439c86fcdfbd5cfff5dbbafd02739146.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [string]$WasmToolsPath,
    [switch]$RequireCompatible
)
$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
& $WasmToolsPath validate $resolvedPath
if ($LASTEXITCODE -ne 0) {
    throw 'wasm-tools rejected the artifact.'
}
$watPath = [IO.Path]::GetTempFileName()
try {
    & $WasmToolsPath print $resolvedPath -o $watPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the artifact.'
    }
    $wat = [IO.File]::ReadAllText($watPath)
}
finally {
    Remove-Item -LiteralPath $watPath -Force
}
$issues = [Collections.Generic.List[string]]::new()
if (-not $wat.StartsWith('(module', [StringComparison]::Ordinal)) {
    $issues.Add('Contour requires a core module, not a component.')
}
$allowedWasi = @(
    'args_get', 'args_sizes_get', 'environ_get', 'environ_sizes_get',
    'clock_time_get', 'clock_res_get', 'fd_write', 'fd_close', 'fd_fdstat_get',
    'fd_prestat_get', 'fd_prestat_dir_name', 'fd_seek', 'random_get', 'proc_exit', 'sched_yield'
)
$imports = @([regex]::Matches($wat, '(?m)^\s*\(import "([^"]+)" "([^"]+)"') | ForEach-Object {
    $importModule = $_.Groups[1].Value
    $importName = $_.Groups[2].Value
    $allowed = ($importModule -ceq 'wasi_snapshot_preview1' -and $importName -cin $allowedWasi) -or
        ($importModule -ceq 'terminal-extensions:statusline/host@0.1.0' -and
            $importName -cin @('state', 'request-frame', 'log', 'run-command'))
    if (-not $allowed) {
        $issues.Add("Unsupported import: $importModule/$importName")
    }
    "$importModule/$importName"
})
foreach ($exportName in @('load', 'update', 'render', 'cabi_realloc', 'memory')) {
    if ($wat -cnotmatch ('\(export "' + [regex]::Escape($exportName) + '" ')) {
        $issues.Add("Missing export: $exportName")
    }
}
$functionTypes = @{}
foreach ($match in [regex]::Matches($wat, '(?m)^\s*\(type \(;(\d+);\) \(func(?: \(param ([^)]*)\))?(?: \(result ([^)]*)\))?\)\)')) {
    $functionTypes[$match.Groups[1].Value] = @($match.Groups[2].Value, $match.Groups[3].Value)
}
$functions = @{}
foreach ($match in [regex]::Matches($wat, '(?m)^\s*\(func (?:\$[^ ]+ )?\(;(\d+);\) \(type (\d+)\)')) {
    $functions[$match.Groups[1].Value] = $match.Groups[2].Value
}
$signatures = @{
    load = @('i32 i32', 'i32')
    update = @('i32 i32 i32 i32 i32', 'i32')
    render = @('i32 i32', 'i32')
    cabi_realloc = @('i32 i32 i32 i32', 'i32')
    cabi_post_render = @('i32', '')
    _initialize = @('', '')
}
foreach ($exportName in $signatures.Keys) {
    $export = [regex]::Match($wat, '\(export "' + [regex]::Escape($exportName) + '" \(func (\d+)\)\)')
    if ($export.Success) {
        $actual = $functionTypes[$functions[$export.Groups[1].Value]]
        $expected = $signatures[$exportName]
        if ($null -eq $actual -or $actual[0] -cne $expected[0] -or $actual[1] -cne $expected[1]) {
            $issues.Add("Incorrect canonical signature: $exportName")
        }
    }
}
if ($wat -match '(?m)^\s*\(start ') {
    $issues.Add('An unguarded start function is not allowed.')
}
$memory = [regex]::Match($wat, '(?m)^\s*\(memory (?:\(;\d+;\) )?(\d+)')
$initialBytes = if ($memory.Success) { [long]$memory.Groups[1].Value * 65536 } else { 0 }
if ($initialBytes -gt 32MB) {
    $issues.Add('Initial linear memory exceeds 32 MiB.')
}
[pscustomobject]@{
    Path = $resolvedPath
    Compatible = $issues.Count -eq 0
    Bytes = (Get-Item -LiteralPath $resolvedPath).Length
    InitialMemoryBytes = $initialBytes
    Imports = $imports
    Issues = $issues.ToArray()
}
if ($RequireCompatible.IsPresent -and $issues.Count -gt 0) {
    throw ($issues -join [Environment]::NewLine)
}
