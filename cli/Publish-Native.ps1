[CmdletBinding()]
param(
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$platformName = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
$architectureName = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = "$platformName-$architectureName"
}
if (-not $RuntimeIdentifier.StartsWith("$platformName-", [StringComparison]::Ordinal)) {
    throw 'Native AOT must be published on the target operating system.'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot "../artifacts/native/$RuntimeIdentifier"
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot '../module/StatusLine.psd1')
Push-Location $PSScriptRoot
try {
    $arguments = @(
        'publish', 'StatusLine.Cli.csproj', '--configuration', 'Release',
        '--runtime', $RuntimeIdentifier, '--output', $OutputPath,
        "-p:Version=$($manifest.ModuleVersion)", "-bl:$OutputPath/publish.binlog"
    )
    & dotnet @arguments
    if ($LASTEXITCODE) { throw "Native publish failed with exit code $LASTEXITCODE." }
    $binaryName = if ($IsWindows) { 'statusline.exe' } else { 'statusline' }
    $binaryPath = Join-Path $OutputPath $binaryName
    & $binaryPath --help
    if ($LASTEXITCODE) { throw 'Published executable smoke test failed.' }
    & dotnet build tests/NativeCli.Tests.csproj --configuration Release "-bl:$OutputPath/tests.binlog"
    if ($LASTEXITCODE) { throw 'Native executable test build failed.' }
    & dotnet run --no-build --project tests/NativeCli.Tests.csproj --configuration Release -- $binaryPath (Join-Path $OutputPath 'test-results.json')
    if ($LASTEXITCODE) { throw 'Native executable tests failed.' }
    $packageName = "statusline-$($manifest.ModuleVersion)-$RuntimeIdentifier"
    $extension = if ($IsWindows) { 'zip' } else { 'tar.gz' }
    $archivePath = Join-Path $OutputPath "$packageName.$extension"
    if ($IsWindows) {
        Compress-Archive -LiteralPath $binaryPath -DestinationPath $archivePath -Force
    }
    else {
        # Tar preserves the executable bit, which artifact ZIP downloads do not retain.
        $archive = [IO.File]::Create($archivePath)
        try {
            $gzip = [IO.Compression.GZipStream]::new($archive, [IO.Compression.CompressionMode]::Compress, $true)
            try {
                $writer = [System.Formats.Tar.TarWriter]::new($gzip, $true)
                try { $writer.WriteEntry($binaryPath, $binaryName) }
                finally { $writer.Dispose() }
            }
            finally { $gzip.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$checksum  $([IO.Path]::GetFileName($archivePath))`n", [Text.UTF8Encoding]::new($false))
}
finally {
    Pop-Location
}
