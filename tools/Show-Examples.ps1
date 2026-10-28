#Requires -Version 7.6
param(
    [ValidateRange(40, 160)]
    [int]$Width = 88,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../output/StatusLine.psd1') -ErrorAction Stop
$savedDirectoryStyle = [StatusLine.DirectoryDisplay]::Style
try {
    [StatusLine.DirectoryDisplay]::Style = 'Leaf'
    $samples = @(
        @{
            Name = 'Plain'; Group = 'prompts'
            Text = (Show-Prompt -Width $Width -Configuration @{
                Left = @('Directory', 'Git'); Right = @(); IconSet = 'None'; Style = 'Plain'; Symbol = ' > '
            }) + 'Get-ChildItem'
        }
        @{
            Name = 'Powerline'; Group = 'prompts'
            Text = (Show-Prompt -Width $Width -ExitCode 0 -Duration ([timespan]::FromSeconds(2.4)) -Configuration @{
                Left = @('Directory', 'Git', 'Status', 'Duration'); Right = @(); Symbol = ' > '
                StartColor = '#24475E'; EndColor = '#5DAA9A'
            }) + 'dotnet test'
        }
        @{
            Name = 'Two rows'; Group = 'prompts'
            Text = (Show-Prompt -Width $Width -ExitCode 0 -Duration ([timespan]::FromSeconds(2.4)) -Configuration @{
                Left = @('Directory', 'Git'); Right = @('PowerShell', 'Duration'); Rows = 2; ConnectRows = $true
                Symbol = '> '; Style = 'Rounded'; StartColor = '#3B315E'; EndColor = '#8573AD'
            }) + 'git status --short'
        }
        @{
            Name = 'Transient'; Group = 'prompts'
            Text = (Show-Prompt -Width $Width -Transient -ExitCode 0 -Configuration @{
                TransientBlock = @('Status'); TransientSymbol = ' > '; IconSet = 'Unicode'
            }) + 'dotnet build'
        }
        @{
            Name = 'System'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -Configuration @{
                Left = @('Cpu', 'SystemMemory', 'ProcessMemory'); Right = @('PowerShell', 'Clock')
                Style = 'Powerline'; StartColor = '#24475E'; EndColor = '#5DAA9A'
            }
        }
        @{
            Name = 'Project'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -ExitCode 0 -Duration ([timespan]::FromSeconds(2.4)) -Configuration @{
                Left = @('Directory', 'Git'); Right = @('Duration', 'PowerShell')
                Style = 'Rounded'; StartColor = '#795332'; EndColor = '#D8AF70'
            }
        }
    )
}
finally {
    [StatusLine.DirectoryDisplay]::Style = $savedDirectoryStyle
}

if ($AsJson.IsPresent) {
    $samples | ConvertTo-Json -Depth 4
}
else {
    foreach ($sample in $samples) {
        Write-Host "`n$($sample.Name)" -ForegroundColor DarkGray
        Write-Host $sample.Text
    }
}
