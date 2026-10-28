#!/usr/bin/env pwsh
#Requires -Version 7.6 -Modules InvokeBuild
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipHelp,
    [switch]$SkipTests
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
    CsprojPath       = Join-Path $PSScriptRoot 'src' "$moduleName.csproj"
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

task All -Jobs Clean, Build, ModuleFiles, GenerateHelp, CleanAfter, Test, DotNetTest
task BuildAndTest -Jobs Clean, Build, ModuleFiles, CleanAfter, Test, DotNetTest
