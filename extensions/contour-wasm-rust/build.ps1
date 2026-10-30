#Requires -Version 7.4

<#
.SYNOPSIS
Builds and validates the unshipped Rust statusline painter.
.DESCRIPTION
Uses the locked dependencies and Release WASI preview-1 target. Copies the artifact to
artifacts/contour only after tests and Contour preflight pass. Does not edit terminal configuration.
.PARAMETER WasmToolsPath
Path to a wasm-tools executable for binary validation and contract preflight.
.EXAMPLE
./build.ps1 -WasmToolsPath /path/to/wasm-tools
.OUTPUTS
System.IO.FileInfo
.NOTES
Requires Rust with the wasm32-wasip1 target. Normal module builds neither build nor stage it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$WasmToolsPath
)
$ErrorActionPreference = 'Stop'
$resolvedTool = (Resolve-Path -LiteralPath $WasmToolsPath).Path
Push-Location -LiteralPath $PSScriptRoot
try {
    cargo build --release --target wasm32-wasip1 --locked
    if ($LASTEXITCODE -ne 0) { throw 'Rust WASM build failed.' }
    cargo test --release --locked
    if ($LASTEXITCODE -ne 0) { throw 'Rust adapter tests failed.' }
    $report = & ../Test-ContourModule.ps1 -Path target/wasm32-wasip1/release/statusline_contour.wasm -WasmToolsPath $resolvedTool -RequireCompatible
    # Outside the module source tree so the experimental asset cannot be staged or committed by accident.
    $destination = Join-Path $PSScriptRoot '../../artifacts/contour'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $report.Path -Destination "$destination/statusline.wasm" -Force
    Get-Item -LiteralPath "$destination/statusline.wasm"
}
finally {
    Pop-Location
}
