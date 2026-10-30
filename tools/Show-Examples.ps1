#Requires -Version 7.6
param(
    [ValidateRange(40, 160)]
    [int]$Width = 96,
    [switch]$AsJson
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../output/StatusLine.psd1') -ErrorAction Stop
$savedDirectoryStyle = [StatusLine.DirectoryDisplay]::Style
$exampleBlocks = @{}
try {
    [StatusLine.DirectoryDisplay]::Style = 'Leaf'
    # Fixed values illustrate blocks that may be absent in the current session.
    foreach ($entry in @(
        @{ Name = 'Errors'; Text = '2'; Icon = "`u{F071}" }
        @{ Name = 'StackCount'; Text = '1'; Icon = "`u{F115}" }
        @{ Name = 'NestedPrompt'; Text = '1'; Icon = "`u{F003D}" }
        @{ Name = 'Runspaces'; Text = '6'; Icon = "`u{E683}" }
        @{ Name = 'HistoryId'; Text = '42'; Icon = "`u{F292}" }
        @{ Name = 'Git'; Text = 'main'; Icon = "`u{F062C}" }
        @{ Name = 'Weather'; Text = '18°C'; Icon = "`u{E30D}" }
        @{ Name = 'Uptime'; Text = '2d 4h'; Icon = "`u{F011}" }
        @{ Name = 'Cpu'; Text = '18%'; Icon = "`u{F4BC}" }
        @{ Name = 'SystemMemory'; Text = '8.2/32 GiB'; Icon = "`u{F035B}" }
    )) {
        $exampleBlocks[$entry.Name] = New-StatusLineBlock -Name "example$($entry.Name)" -Icon ($entry.Icon ?? '') -ScriptBlock ([scriptblock]::Create("'$($entry.Text.Replace("'", "''"))'"))
    }
    $sessionLayout = @{
        Left = @($exampleBlocks.Errors, $exampleBlocks.StackCount, $exampleBlocks.NestedPrompt, $exampleBlocks.Runspaces, $exampleBlocks.Git)
        Center = @($exampleBlocks.Weather, $exampleBlocks.Uptime)
        Right = @($exampleBlocks.Cpu, $exampleBlocks.SystemMemory)
    }
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
            Name = 'Session · sample counts'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -Configuration @{
                Left = @($exampleBlocks.Errors, $exampleBlocks.StackCount, $exampleBlocks.NestedPrompt, $exampleBlocks.Runspaces)
                Right = @('PowerShell', $exampleBlocks.HistoryId)
                Style = 'Powerline'; StartColor = '#24475E'; EndColor = '#5DAA9A'
            }
        }
        @{
            Name = 'Project'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -ExitCode 0 -Duration ([timespan]::FromSeconds(2.4)) -Configuration @{
                Left = @('Directory', 'Git'); Right = @('Duration', 'Clock')
                Style = 'Rounded'; StartColor = '#795332'; EndColor = '#D8AF70'
            }
        }
        @{
            Name = 'Muted gradient'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -Configuration @{
                Left = @('Directory', 'Git'); Center = @('PowerShell'); Right = @('Clock')
                Style = 'Rounded'; BlendBackground = $true; StartColor = '#174B58'; EndColor = '#B18AD2'
            }
        }
        @{
            Name = 'Blue → orange gradient · sample values'; Group = 'statuslines'
            Text = Show-StatusLine -Width $Width -Configuration (@{} + $sessionLayout + @{
                Style = 'Rounded'; BlendBackground = $true; StartColor = '#0400fdff'; EndColor = '#ff7b00ff'
            })
        }
    )
}
finally {
    foreach ($block in $exampleBlocks.Values) { $block.Dispose() }
    [StatusLine.DirectoryDisplay]::Style = $savedDirectoryStyle
}

foreach ($sample in $samples) { $sample.Width = $Width }

if ($AsJson.IsPresent) {
    $samples | ConvertTo-Json -Depth 4
}
else {
    foreach ($sample in $samples) {
        Write-Host "`n$($sample.Name)" -ForegroundColor DarkGray
        Write-Host $sample.Text
    }
}
