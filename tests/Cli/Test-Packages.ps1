<#
.SYNOPSIS
Tests Linux package installation and removal in clean runtime containers.
.DESCRIPTION
Installs the generated packages with dependency resolution on Ubuntu, Debian, and Fedora.
Checks installed rendering without .NET, executable permissions, examples, and profile preservation.
.PARAMETER Path
Directory containing the Linux deb and rpm packages.
.PARAMETER Version
Expected package version.
.PARAMETER Engine
Docker by default; Podman is supported for local validation.
.EXAMPLE
./tests/Cli/Test-Packages.ps1 -Path ./artifacts/native/linux-x64 -Version 0.2.0
.EXAMPLE
./tests/Cli/Test-Packages.ps1 -Path ./artifacts/native/linux-x64 -Version 0.2.0 -Engine podman
.OUTPUTS
None. Throws if installation or validation fails.
.NOTES
Requires a Linux x64 container engine and network access for distribution dependencies.
Containers are removed after the test. No host packages or shell profiles are changed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,
    [Parameter(Mandatory)]
    [version]$Version,
    [ValidateSet('docker', 'podman')]
    [string]$Engine = 'docker'
)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $Path).Path
$profileFixture = [IO.Path]::GetTempFileName()
[IO.File]::WriteAllText($profileFixture, "# StatusLine package test: preserve this profile`n")
$profileHash = (Get-FileHash -LiteralPath $profileFixture -Algorithm SHA256).Hash.ToLowerInvariant()
$results = [Collections.Generic.List[object]]::new()
$run = {
    param([string[]]$Arguments)
    & $Engine @Arguments
    if ($LASTEXITCODE) { throw "$Engine $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}
try {
    foreach ($target in @(
        @{ Image = 'docker.io/library/ubuntu:24.04'; Format = 'deb' }
        @{ Image = 'docker.io/library/debian:bookworm-slim'; Format = 'deb' }
        @{ Image = 'registry.fedoraproject.org/fedora:43'; Format = 'rpm' }
    )) {
        $containerId = $null
        $passed = $false
        $failure = $null
        try {
            $package = Join-Path $packageRoot "statusline-$Version-linux-x64.$($target.Format)"
            if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw "Missing package: $package" }
            $containerId = (& $run @('create', '--platform', 'linux/amd64', '--env', 'DEBIAN_FRONTEND=noninteractive', '--entrypoint', 'sleep', $target.Image, 'infinity') | Select-Object -Last 1).Trim()
            & $run @('cp', $package, "${containerId}:/tmp/statusline.$($target.Format)")
            & $run @('cp', $profileFixture, "${containerId}:/root/.bashrc")
            & $run @('start', $containerId)
            if ($target.Format -eq 'deb') {
                & $run @('exec', $containerId, 'apt-get', 'update', '-qq')
                # Minimal Debian/Ubuntu images discard docs by default; include them to test the shipped examples.
                & $run @('exec', $containerId, 'apt-get', 'install', '-y', '-o', 'Dpkg::Options::=--path-include=/usr/share/doc/*', '/tmp/statusline.deb')
            }
            else {
                & $run @('exec', $containerId, 'dnf', '-y', 'install', '/tmp/statusline.rpm')
            }
            & $run @('exec', $containerId, 'test', '-x', '/usr/bin/statusline')
            & $run @('exec', $containerId, 'test', '-f', '/usr/share/doc/statusline/copyright')
            $reported = (& $run @('exec', '--user', '65534:65534', $containerId, '/usr/bin/statusline', '--version')) -join "`n"
            if (-not $reported.StartsWith($Version.ToString(), [StringComparison]::Ordinal)) { throw "Unexpected executable version: $reported" }
            $prompt = (& $run @('exec', '--user', '65534:65534', $containerId, '/usr/bin/statusline', 'prompt', '--config', '/usr/share/doc/statusline/examples/minimal.json', '--cwd', '/tmp')) -join "`n"
            if ($prompt -notmatch 'tmp') { throw 'Installed prompt did not render the working directory.' }
            if ($target.Format -eq 'deb') { & $run @('exec', $containerId, 'apt-get', 'remove', '-y', 'statusline') }
            else { & $run @('exec', $containerId, 'dnf', '-y', 'remove', 'statusline') }
            & $run @('exec', $containerId, 'test', '!', '-e', '/usr/bin/statusline')
            $profileRecord = (& $run @('exec', $containerId, 'sha256sum', '/root/.bashrc')) -join ''
            if (-not $profileRecord.StartsWith($profileHash, [StringComparison]::Ordinal)) { throw 'Package installation/removal modified the shell profile.' }
            $passed = $true
            Write-Information "PASS package install/render/remove: $($target.Image)" -InformationAction Continue
        }
        catch { $failure = $_.Exception.Message; throw }
        finally {
            $results.Add([pscustomobject]@{ Image = $target.Image; Passed = $passed; Error = $failure })
            if ($containerId) { & $Engine rm --force $containerId | Out-Null }
        }
    }
}
finally {
    Remove-Item -LiteralPath $profileFixture -Force
    ConvertTo-Json -InputObject $results.ToArray() -Depth 3 | Set-Content -LiteralPath (Join-Path $packageRoot 'package-test-results.json') -Encoding utf8NoBOM
}
