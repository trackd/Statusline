<#
.SYNOPSIS
Downloads pinned terminal integration fixtures for compatibility checks.
.DESCRIPTION
Saves unmodified upstream scripts in the build artifacts directory. These scripts
are executed only in Linux test PTYs and are not included in the native package.
.PARAMETER OutputPath
Directory in which to save Contour and Ghostty fixtures.
.EXAMPLE
./cli/tests/Save-UpstreamIntegration.ps1
.EXAMPLE
./cli/tests/Save-UpstreamIntegration.ps1 -OutputPath ./artifacts/terminal-fixtures
.OUTPUTS
System.String. The absolute fixture directory.
.NOTES
Revisions match docs/ShellIntegration-Compatibility.md. Downloads use immutable commits.
#>
[CmdletBinding()]
param([string]$OutputPath = (Join-Path $PSScriptRoot '../../artifacts/terminal-integration'))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$sources = @(
    @{
        Name = 'contour'
        Base = 'https://raw.githubusercontent.com/contour-terminal/contour/4918660e7e04e8d507fb45396ef36934835fd57b/src/contour/cli/shell-integration'
        Files = @{ 'shell-integration.bash' = 'shell-integration.bash'; 'shell-integration.zsh' = 'shell-integration.zsh'; 'shell-integration.fish' = 'shell-integration.fish' }
    },
    @{
        Name = 'ghostty'
        Base = 'https://raw.githubusercontent.com/ghostty-org/ghostty/df1c99c8a5ec32c73963e5dc3fad3c9df7a3d137/src/shell-integration'
        Files = @{ 'bash--ghostty.bash' = 'bash/ghostty.bash'; 'zsh--ghostty-integration' = 'zsh/ghostty-integration'; 'fish--vendor_conf.d--ghostty-shell-integration.fish' = 'fish/vendor_conf.d/ghostty-shell-integration.fish' }
    }
)
foreach ($source in $sources) {
    $directoryPath = Join-Path $resolvedOutput $source.Name
    New-Item -ItemType Directory -Path $directoryPath -Force | Out-Null
    foreach ($entry in $source.Files.GetEnumerator()) {
        Invoke-WebRequest -Uri "$($source.Base)/$($entry.Value)" -OutFile (Join-Path $directoryPath $entry.Key) -MaximumRetryCount 3 -RetryIntervalSec 2
    }
}
$resolvedOutput
