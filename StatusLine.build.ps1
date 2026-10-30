#!/usr/bin/env pwsh
#Requires -Version 7.6 -Modules InvokeBuild
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests,
    [ValidateSet('linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$NativeOutputPath,
    [ValidateSet('Size', 'Speed')]
    [string]$OptimizationPreference = 'Speed',
    [switch]$VerifyTerminalIntegration
)

$ErrorActionPreference = 'Stop'
$moduleName = [IO.Path]::GetFileName($PSCommandPath) -replace '\.build\.ps1$'
$script:folders = @{
    ModuleName       = $moduleName
    ProjectRoot      = $PSScriptRoot
    OutputPath       = Join-Path $PSScriptRoot 'output'
    ModuleSourcePath = Join-Path $PSScriptRoot 'module'
    DocsPath         = Join-Path $PSScriptRoot 'docs' 'en-US'
    TestPath         = Join-Path $PSScriptRoot 'tests'
    TestResultPath   = Join-Path $PSScriptRoot 'artifacts' 'test-results'
    CsprojPath       = Join-Path $PSScriptRoot 'src' 'PowerShell' "$moduleName.csproj"
    TestCsprojPath   = Join-Path $PSScriptRoot 'tests' "$moduleName.Tests" "$moduleName.Tests.csproj"
}

task Clean {
    if (Test-Path -LiteralPath $folders.OutputPath) {
        Remove-Item -LiteralPath $folders.OutputPath -Recurse -Force
    }
    New-Item -Path $folders.OutputPath -ItemType Directory -Force | Out-Null
}

task Build {
    if (-not (Test-Path $folders.CsprojPath)) {
        Write-Warning 'C# project not found, skipping Build'
        return
    }

    $ModuleFile = Get-Module (Join-Path $script:folders.ProjectRoot 'module' "$($script:folders.ModuleName).psd1") -ListAvailable
    [xml]$csproj = Get-Content -Path $folders.CsprojPath -Raw
    $frameworks = $csproj.
    SelectNodes('//TargetFramework | //TargetFrameworks').
    '#text'.
    Split(';', [StringSplitOptions]::RemoveEmptyEntries)

    $dotnetArgs = @(
        'publish'
        $folders.CsprojPath
        '--configuration', $Configuration
        '--nologo'
        '--verbosity', 'minimal'
        ('-p:Version={0}' -f $ModuleFile.Version.ToString())
    )

    foreach ($fwork in $frameworks) {
        exec { dotnet @dotnetArgs --framework $fwork --output $folders.OutputPath }
    }
}

task ModuleFiles {
    Get-ChildItem -LiteralPath $folders.ModuleSourcePath -File |
        Copy-Item -Destination $folders.OutputPath -Force
}

task GenerateHelp -If (-not $SkipHelp.IsPresent) {
    if (-not (Get-Module -ListAvailable -Name Microsoft.PowerShell.PlatyPS)) {
        Install-Module -Name Microsoft.PowerShell.PlatyPS -Scope CurrentUser -Force -AllowClobber
    }
    Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop

    $helpOutputPath = Join-Path $folders.OutputPath 'en-US'
    $tempOutputPath = Join-Path $helpOutputPath 'temp'
    # Start fresh because PlatyPS can leave trailing XML when overwriting a longer file.
    if (Test-Path -LiteralPath $tempOutputPath) {
        Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
    }
    $commandHelp = @(Get-ChildItem -LiteralPath $folders.DocsPath -Filter '*.md' -File |
        Where-Object { $_.Name -ne "$moduleName.md" } |
        Import-MarkdownCommandHelp)
    Export-MamlCommandHelp -CommandHelp $commandHelp -OutputFolder $tempOutputPath | Out-Null
    Get-ChildItem -LiteralPath $tempOutputPath -Filter '*.xml' -File -Recurse |
        Move-Item -Destination $helpOutputPath -Force
    Remove-Item -LiteralPath $tempOutputPath -Recurse -Force
}

task CleanAfter {
    Get-ChildItem -LiteralPath $folders.OutputPath -File -Recurse |
        Where-Object { $_.Extension -in @('.pdb', '.json') } |
        Remove-Item -Force
}

task Test -If (-not $SkipTests.IsPresent) {
    Import-Module Pester -MinimumVersion 6.0.0 -ErrorAction Stop
    $pesterConfig = New-PesterConfiguration
    $pesterConfig.Run.Path = $folders.TestPath
    $pesterConfig.Run.Throw = $true
    $pesterConfig.Output.CIFormat = 'Auto'
    $pesterConfig.TestResult.Enabled = $true
    $pesterConfig.TestResult.OutputFormat = 'JUnitXml'
    $pesterConfig.TestResult.OutputPath = Join-Path $folders.TestResultPath 'pester.xml'
    New-Item -Path $folders.TestResultPath -ItemType Directory -Force | Out-Null
    Invoke-Pester -Configuration $pesterConfig
}

task DotNetTest -If (-not $SkipTests.IsPresent) {
    exec { dotnet test $folders.TestCsprojPath --configuration $Configuration --nologo --logger 'trx;LogFileName=dotnet.trx' --results-directory $folders.TestResultPath }
}

task NativeBuild {
    # Managed build runs trim/AOT analysis without native compilation.
    exec { dotnet build "$PSScriptRoot/src/Cli/StatusLine.Cli.csproj" --configuration $Configuration "-bl:$PSScriptRoot/artifacts/cli-build.binlog" }
}

task NativePrepare {
    $platformName = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $architectureName = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $script:nativeRuntime = if ($RuntimeIdentifier) { $RuntimeIdentifier } else { "$platformName-$architectureName" }
    if (-not $nativeRuntime.StartsWith("$platformName-", [StringComparison]::Ordinal)) {
        throw 'Native AOT must be published on the target operating system.'
    }
    if ($VerifyTerminalIntegration.IsPresent -and -not $IsLinux) {
        throw 'Terminal integration PTY checks currently require Linux.'
    }
    $script:nativeOutput = if ($NativeOutputPath) { [IO.Path]::GetFullPath($NativeOutputPath) } else { Join-Path $PSScriptRoot "artifacts/native/$nativeRuntime" }
    New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
    $script:nativeManifest = Import-PowerShellDataFile (Join-Path $folders.ModuleSourcePath 'StatusLine.psd1')
    $binaryName = if ($IsWindows) { 'statusline.exe' } else { 'statusline' }
    $script:nativeBinary = Join-Path $nativeOutput $binaryName
}

task NativePublish -Jobs NativePrepare, {
    $arguments = @(
        'publish', "$PSScriptRoot/src/Cli/StatusLine.Cli.csproj", '--configuration', $Configuration,
        '--runtime', $nativeRuntime, '--output', $nativeOutput,
        "-p:Version=$($nativeManifest.ModuleVersion)", "-p:OptimizationPreference=$OptimizationPreference", "-bl:$nativeOutput/publish.binlog"
    )
    exec { dotnet @arguments }
    exec { & $nativeBinary --help }
}

task NativeTest -Jobs NativePublish, {
    if ($SkipTests.IsPresent) { return }
    $previousUpstreamRoot = $env:STATUSLINE_UPSTREAM_INTEGRATION_ROOT
    try {
        if ($VerifyTerminalIntegration.IsPresent) {
            $env:STATUSLINE_UPSTREAM_INTEGRATION_ROOT = & "$PSScriptRoot/tests/Cli/Save-UpstreamIntegration.ps1"
        }
        exec { dotnet build "$PSScriptRoot/tests/Cli/NativeCli.Tests.csproj" --configuration $Configuration "-bl:$nativeOutput/tests.binlog" }
        exec { dotnet run --no-build --project "$PSScriptRoot/tests/Cli/NativeCli.Tests.csproj" --configuration $Configuration -- $nativeBinary (Join-Path $nativeOutput 'test-results.json') }
    }
    finally { $env:STATUSLINE_UPSTREAM_INTEGRATION_ROOT = $previousUpstreamRoot }
}

task NativeArchive -Jobs NativeTest, {
    $packageName = "statusline-$($nativeManifest.ModuleVersion)-$nativeRuntime"
    $extension = if ($IsWindows) { 'zip' } else { 'tar.gz' }
    $archivePath = Join-Path $nativeOutput "$packageName.$extension"
    if ($IsWindows) {
        Compress-Archive -LiteralPath $nativeBinary -DestinationPath $archivePath -Force
    }
    else {
        $archive = [IO.File]::Create($archivePath)
        try {
            $gzip = [IO.Compression.GZipStream]::new($archive, [IO.Compression.CompressionMode]::Compress, $true)
            try {
                $writer = [System.Formats.Tar.TarWriter]::new($gzip, $true)
                try { $writer.WriteEntry($nativeBinary, 'statusline') }
                finally { $writer.Dispose() }
            }
            finally { $gzip.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$checksum  $([IO.Path]::GetFileName($archivePath))`n", [Text.UTF8Encoding]::new($false))
}

task NativeLinuxPackage -Jobs NativeArchive, {
    if (-not $IsLinux) { return }
    $nfpm = & "$PSScriptRoot/tools/Get-Nfpm.ps1"
    $architecture = switch (($nativeRuntime -split '-')[-1]) { 'x64' { 'amd64' } 'arm64' { 'arm64' } default { throw 'Unsupported Linux package architecture.' } }
    $elfVersions = exec { readelf --version-info $nativeBinary }
    $glibcVersion = [regex]::Matches(($elfVersions -join "`n"), 'GLIBC_(\d+\.\d+)') |
        ForEach-Object { [version]$_.Groups[1].Value } | Sort-Object -Descending | Select-Object -First 1
    if (-not $glibcVersion) { throw 'Could not determine the executable glibc requirement.' }
    $contents = @(
        @{ src = $nativeBinary; dst = '/usr/bin/statusline'; file_info = @{ mode = 493 } }
        @{ src = "$PSScriptRoot/LICENSE"; dst = '/usr/share/doc/statusline/copyright'; file_info = @{ mode = 420 } }
    )
    foreach ($example in Get-ChildItem "$PSScriptRoot/docs/examples/cli" -Filter '*.json' -File) {
        $contents += @{ src = $example.FullName; dst = "/usr/share/doc/statusline/examples/$($example.Name)"; file_info = @{ mode = 420 } }
    }
    $packageConfig = @{
        name = 'statusline'; arch = $architecture; platform = 'linux'; version = $nativeManifest.ModuleVersion; release = '1'
        maintainer = 'trackd <17672644+trackd@users.noreply.github.com>'
        description = 'Configurable shell prompt and asynchronous terminal status bar.'
        homepage = 'https://github.com/trackd/Statusline'; license = 'MIT'; section = 'utils'; contents = $contents
        overrides = @{
            deb = @{ depends = @("libc6 (>= $glibcVersion)", 'libgcc-s1', 'libstdc++6', 'libicu74 | libicu72 | libicu76 | libicu78 | libicu70', 'libssl3t64 | libssl3', 'zlib1g', 'ca-certificates') }
            rpm = @{ depends = @("glibc >= $glibcVersion", 'libgcc', 'libstdc++', 'libicu', 'openssl-libs', 'zlib', 'ca-certificates') }
        }
    }
    $configPath = Join-Path $nativeOutput 'nfpm.json'
    $packageConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding utf8NoBOM
    foreach ($format in 'deb', 'rpm') {
        $packagePath = Join-Path $nativeOutput "statusline-$($nativeManifest.ModuleVersion)-$nativeRuntime.$format"
        exec { & $nfpm package --config $configPath --packager $format --target $packagePath }
        $checksum = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$packagePath.sha256", "$checksum  $([IO.Path]::GetFileName($packagePath))`n", [Text.UTF8Encoding]::new($false))
    }
}

task NativeMacPackage -Jobs NativeArchive, {
    if (-not $IsMacOS) { return }
    $packagePath = Join-Path $nativeOutput "statusline-$($nativeManifest.ModuleVersion)-$nativeRuntime.pkg"
    $stagingRoot = Join-Path $nativeOutput ("pkg-root-" + [guid]::NewGuid().ToString('N'))
    $binaryDirectory = Join-Path $stagingRoot 'usr/local/bin'
    $documentationDirectory = Join-Path $stagingRoot 'usr/local/share/doc/statusline'
    try {
        New-Item -ItemType Directory -Path $binaryDirectory, "$documentationDirectory/examples" -Force | Out-Null
        Copy-Item -LiteralPath $nativeBinary -Destination "$binaryDirectory/statusline"
        Copy-Item -LiteralPath "$PSScriptRoot/LICENSE" -Destination "$documentationDirectory/LICENSE"
        Get-ChildItem "$PSScriptRoot/docs/examples/cli" -Filter '*.json' -File |
            Copy-Item -Destination "$documentationDirectory/examples"
        exec { chmod 755 "$binaryDirectory/statusline" }
        exec { chmod -R 'u=rwX,go=rX' $stagingRoot }
        exec { pkgbuild --root $stagingRoot --identifier io.github.trackd.statusline --version $nativeManifest.ModuleVersion --install-location / --ownership recommended $packagePath }
        $checksum = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$packagePath.sha256", "$checksum  $([IO.Path]::GetFileName($packagePath))`n", [Text.UTF8Encoding]::new($false))
    }
    finally {
        $resolvedRoot = [IO.Path]::GetFullPath($stagingRoot)
        $outputPrefix = [IO.Path]::GetFullPath($nativeOutput).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedRoot.StartsWith($outputPrefix, [StringComparison]::Ordinal)) { throw 'Package staging path escaped the output directory.' }
        if (Test-Path -LiteralPath $resolvedRoot) { Remove-Item -LiteralPath $resolvedRoot -Recurse -Force }
    }
}

task NativePackage -Jobs NativeLinuxPackage, NativeMacPackage

task Native -Jobs NativePackage

task All -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter, Test, DotNetTest
task BuildAndTest -Jobs Clean, Build, ModuleFiles, CleanAfter, Test, DotNetTest
