<#
.SYNOPSIS
Gets the pinned Linux package builder.
.DESCRIPTION
Downloads nFPM into the ignored tools cache and verifies its pinned SHA256 before extraction.
.EXAMPLE
./tools/Get-Nfpm.ps1
.EXAMPLE
$nfpm = & ./tools/Get-Nfpm.ps1
& $nfpm --version
.OUTPUTS
System.String. Absolute executable path.
.NOTES
Used only by explicit Linux packaging tasks; does not modify PATH or install system packages.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'The Linux package builder requires Linux.' }
$version = '2.48.0'
$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$archives = @{
    x64 = @{ Name = 'x86_64'; Hash = '6d7f9a28b2f397934403950fcbba83f598b08d02edbe79a0815b3b4897341750' }
    arm64 = @{ Name = 'arm64'; Hash = 'a9a75213763783b7669719e505474c748c5ad7dc66591a195619deb50f85c2f4' }
}
if (-not $archives.ContainsKey($architecture)) { throw "Unsupported package builder architecture: $architecture" }
$metadata = $archives[$architecture]
$cachePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../artifacts/tools/nfpm/$version/$architecture"))
New-Item -ItemType Directory -Path $cachePath -Force | Out-Null
$archivePath = Join-Path $cachePath 'nfpm.tar.gz'
if (-not (Test-Path -LiteralPath $archivePath)) {
    Invoke-WebRequest -Uri "https://github.com/goreleaser/nfpm/releases/download/v$version/nfpm_${version}_Linux_$($metadata.Name).tar.gz" -OutFile $archivePath -MaximumRetryCount 3 -RetryIntervalSec 2
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ine $metadata.Hash) { throw 'nFPM archive checksum mismatch.' }
$stream = [IO.File]::OpenRead($archivePath)
try {
    $gzip = [IO.Compression.GZipStream]::new($stream, [IO.Compression.CompressionMode]::Decompress)
    try { [System.Formats.Tar.TarFile]::ExtractToDirectory($gzip, $cachePath, $true) }
    finally { $gzip.Dispose() }
}
finally { $stream.Dispose() }
$binaryPath = Join-Path $cachePath 'nfpm'
[IO.File]::SetUnixFileMode($binaryPath, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherExecute)
$binaryPath
