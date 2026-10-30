#Requires -Version 7.4

<#
.SYNOPSIS
Builds the experimental C# statusline adapter and reports Contour preflight compatibility.
.DESCRIPTION
Builds Release only, then checks the core module against the pinned Contour contract.
Does not install the extension or modify Contour configuration. A failed preflight is reported as data.
.EXAMPLE
./build.ps1
.OUTPUTS
System.Management.Automation.PSCustomObject
.NOTES
Requires .NET 10. The first build downloads the pinned experimental compiler and WASI SDK.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet build StatusLine.Contour.csproj --configuration Release -p:IlcLlvmTarget=wasm32-unknown-wasip1 -p:LinkerFlavor=lld
    if ($LASTEXITCODE -ne 0) {
        throw 'C# WASM build failed.'
    }
    $assets = Get-Content -LiteralPath obj/project.assets.json -Raw | ConvertFrom-Json -AsHashtable
    $packageRoot = $assets.packageFolders.Keys | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ 'bytecodealliance.componentize.dotnet.wasm.sdk/0.8.0-preview00011')
    } | Select-Object -First 1
    $platform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $toolName = if ($IsWindows) { 'wasm-tools.exe' } else { 'wasm-tools' }
    $toolPath = Join-Path $packageRoot "bytecodealliance.componentize.dotnet.wasm.sdk/0.8.0-preview00011/tools/$platform-$architecture/$toolName"
    & ../Test-ContourModule.ps1 -Path bin/Release/net10.0/wasi-wasm/publish/statusline.wasm -WasmToolsPath $toolPath
}
finally {
    Pop-Location
}
