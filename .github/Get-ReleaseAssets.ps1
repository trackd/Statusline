<#
.SYNOPSIS
Validates and returns the packages to attach to a release.
.DESCRIPTION
Requires each configured native package and matching SHA256 file for every target.
The module package is also required unless NativeOnly is specified.
.PARAMETER Version
Shared module and CLI version.
.PARAMETER Path
Directory containing downloaded workflow artifacts.
.PARAMETER NativeOnly
Select only native packages for attachment to an existing release.
.EXAMPLE
./.github/Get-ReleaseAssets.ps1 -Version 0.3.0 -Path ./artifacts/release
.EXAMPLE
./.github/Get-ReleaseAssets.ps1 -Version 0.2.0 -Path ./artifacts/native -NativeOnly
.OUTPUTS
System.IO.FileInfo. Validated release assets.
.NOTES
Does not publish packages or modify releases.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [version]$Version,
    [Parameter(Mandatory)]
    [string]$Path,
    [switch]$NativeOnly
)
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem -LiteralPath $Path -File -Recurse)
$expected = [Collections.Generic.List[string]]::new()
$targets = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'native-targets.json') -Raw | ConvertFrom-Json).include
foreach ($target in $targets) {
    foreach ($extension in $target.packages) {
        $archiveName = "statusline-$Version-$($target.rid).$extension"
        $expected.Add($archiveName)
        $expected.Add("$archiveName.sha256")
    }
}
if (-not $NativeOnly.IsPresent) { $expected.Add("StatusLine.$Version.nupkg") }
foreach ($name in $expected) {
    $matching = @($files | Where-Object Name -CEQ $name)
    if ($matching.Count -ne 1) { throw "Expected exactly one release asset named $name; found $($matching.Count)." }
}
if (@($files | Where-Object { $_.Name -cnotin $expected }).Count) {
    throw 'Release artifacts contain unexpected files or mismatched versions.'
}
foreach ($checksum in $files | Where-Object Extension -EQ '.sha256') {
    $archiveName = $checksum.Name -replace '\.sha256$', ''
    $archive = $files | Where-Object Name -CEQ $archiveName
    $record = [IO.File]::ReadAllText($checksum.FullName).Trim()
    $match = [regex]::Match($record, '^([0-9a-fA-F]{64})  (.+)$')
    if (-not $match.Success -or $match.Groups[2].Value -cne $archiveName -or
        (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash -ine $match.Groups[1].Value) {
        throw "Invalid checksum for $archiveName."
    }
}
$files | Sort-Object Name
