#!/usr/bin/env pwsh
#Requires -Version 7.6
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests,
    [switch]$BuildAndTestOnly,
    [string]$Task
)

$ErrorActionPreference = 'Stop'
$buildParameters = @{
    Configuration = $Configuration
    SkipHelp      = $SkipHelp.IsPresent
    SkipTests     = $SkipTests.IsPresent
    File          = Join-Path $PSScriptRoot 'StatusLine.build.ps1'
    Task          = 'All'
    Result        = 'buildResult'
    Safe          = $true
}

if (-not (Get-Module -ListAvailable -Name InvokeBuild)) {
    Install-Module -Name InvokeBuild -Scope CurrentUser -Force -AllowClobber
}
Import-Module InvokeBuild -ErrorAction Stop

if ($Task) {
    $buildParameters.Task = $Task
}
elseif ($BuildAndTestOnly.IsPresent) {
    $buildParameters.Task = 'BuildAndTest'
}

# Keep the module assembly out of the calling session so it can be rebuilt.
$worker = {
    param($parameters)
    $ErrorActionPreference = 'Stop'
    Import-Module InvokeBuild -ErrorAction Stop
    Invoke-Build @parameters
    if ($buildResult.Error) { exit 1 }
}
$powerShellPath = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
& $powerShellPath -NoProfile -Command $worker -args $buildParameters
exit $LASTEXITCODE
