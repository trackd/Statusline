<#
.SYNOPSIS
Tests the macOS CLI installer on a clean runner.
.DESCRIPTION
Installs the pkg, checks its receipt and installed prompt, and verifies profiles are unchanged.
Refuses to overwrite an existing installation. Removes the test installation and receipt afterwards.
.PARAMETER Path
Directory containing the installer.
.PARAMETER RuntimeIdentifier
macOS architecture used for the package filename.
.PARAMETER Version
Expected package and executable version.
.EXAMPLE
./tests/Cli/Test-MacPackage.ps1 -Path ./artifacts/native/osx-arm64 -RuntimeIdentifier osx-arm64 -Version 0.2.0
.EXAMPLE
./tests/Cli/Test-MacPackage.ps1 -Path ./artifacts/native/osx-x64 -RuntimeIdentifier osx-x64 -Version 0.3.0
.OUTPUTS
None. Writes package-test-results.json and throws on failure.
.NOTES
Requires macOS and passwordless sudo, as provided by hosted macOS runners.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string]$RuntimeIdentifier,
    [Parameter(Mandatory)]
    [version]$Version
)
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'The macOS installer test requires macOS.' }
$packageRoot = (Resolve-Path -LiteralPath $Path).Path
$packagePath = Join-Path $packageRoot "statusline-$Version-$RuntimeIdentifier.pkg"
$identifier = 'io.github.trackd.statusline'
$binaryPath = '/usr/local/bin/statusline'
$documentationDirectory = '/usr/local/share/doc/statusline'
$run = {
    param([string[]]$Arguments)
    $executable = $Arguments[0]
    & $executable @($Arguments | Select-Object -Skip 1)
    if ($LASTEXITCODE) { throw "$($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "Missing installer: $packagePath" }
$receipts = @(& $run @('/usr/sbin/pkgutil', '--pkgs'))
if ($receipts -contains $identifier -or (Test-Path -LiteralPath $binaryPath) -or (Test-Path -LiteralPath $documentationDirectory)) {
    throw 'The macOS installer test requires a clean runner; an installation already exists.'
}
$userDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$profiles = @{}
foreach ($name in '.profile', '.bashrc', '.bash_profile', '.zshrc', '.config/fish/config.fish', '.config/fish/conf.d/statusline.fish') {
    $profilePath = Join-Path $userDirectory $name
    $profiles[$profilePath] = if (Test-Path -LiteralPath $profilePath) { (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash } else { $null }
}
$started = $false
$passed = $false
$failure = $null
try {
    $started = $true
    & $run @('sudo', '-n', '/usr/sbin/installer', '-pkg', $packagePath, '-target', '/')
    [xml]$receipt = (& $run @('/usr/sbin/pkgutil', '--pkg-info-plist', $identifier)) -join "`n"
    $recordedVersion = $receipt.SelectSingleNode('/plist/dict/key[.="pkg-version"]/following-sibling::*[1]').InnerText
    if ($recordedVersion -cne $Version.ToString()) { throw "Unexpected installer receipt version: $recordedVersion" }
    & $run @('/bin/test', '-x', $binaryPath)
    & $run @('/bin/test', '-f', "$documentationDirectory/LICENSE")
    $reported = (& $run @($binaryPath, '--version')) -join "`n"
    if (-not $reported.StartsWith($Version.ToString(), [StringComparison]::Ordinal)) { throw "Unexpected executable version: $reported" }
    $prompt = (& $run @($binaryPath, 'prompt', '--config', "$documentationDirectory/examples/minimal.json", '--cwd', '/tmp')) -join "`n"
    if ($prompt -notmatch 'tmp') { throw 'The installed CLI did not render the working directory.' }
    foreach ($profilePath in $profiles.Keys) {
        $actual = if (Test-Path -LiteralPath $profilePath) { (Get-FileHash -LiteralPath $profilePath -Algorithm SHA256).Hash } else { $null }
        if ($actual -cne $profiles[$profilePath]) { throw "Installer modified a shell profile: $profilePath" }
    }
    $passed = $true
    Write-Information 'PASS macOS installer receipt, executable, rendering, and profiles' -InformationAction Continue
}
catch { $failure = $_.Exception.Message; throw }
finally {
    try {
        if ($started) {
            if ([IO.Path]::GetFullPath($documentationDirectory) -cne '/usr/local/share/doc/statusline') { throw 'Unsafe installer test cleanup path.' }
            & $run @('sudo', '-n', '/bin/rm', '-f', '--', $binaryPath)
            & $run @('sudo', '-n', '/bin/rm', '-rf', '--', $documentationDirectory)
            if (@(& $run @('/usr/sbin/pkgutil', '--pkgs')) -contains $identifier) {
                & $run @('sudo', '-n', '/usr/sbin/pkgutil', '--forget', $identifier)
            }
        }
    }
    catch { $passed = $false; $failure = $_.Exception.Message; throw }
    finally {
        [pscustomobject]@{ Name = 'MacPackageInstallation'; Passed = $passed; Error = $failure } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'package-test-results.json') -Encoding utf8NoBOM
    }
}
