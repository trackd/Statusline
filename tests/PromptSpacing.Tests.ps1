BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Prompt spacing' -Tag Rendering {
    It 'draws transparent Powerline breaks for <Alignment> blocks' -ForEach @(
        @{ Alignment = 'Left' }; @{ Alignment = 'Right' }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Block = @((New-TestBlock 'A' -Icon 'I' -Padding ' ' -Background Red -Alignment $Alignment), (New-TestBlock 'B' -Icon 'J' -Padding ' ' -Background Blue -Alignment $Alignment))
            Style = 'Powerline'; BlockSeparator = $true; Padding = [string][char]0x2009
            Rows = 2; Symbol = '> '; ContextSymbol = ''; InputLinePrefix = ''; Transient = $false; ShellIntegration = 'None'
        }
        $sequence = Get-TestPrompt -Configuration $configuration -Width 80
        $sequence | Should -Match ([regex]::Escape("$([char]0x2009)I A"))
        $sequence | Should -Match ([regex]::Escape("$([char]0x2009)J B"))
        $cutout = if ($Alignment -eq 'Left') { [char]0xE0D7 } else { [char]0xE0D6 }
        $sequence | Should -Match ('\x1b\[0;38;2;[0-9;]+;49m' + $cutout)
        $sequence | Should -Not -Match '\x1b\[7m'
        $plain = $sequence -replace '\x1b\[[0-9;]*m'
        $plain | Should -Not -Match ' '
    }

    It 'wraps block content in Padding and keeps ordinary icon spacing' {
        $configuration = New-TestPromptConfiguration @{
            Left = @('PowerShell'); Right = @(); Block = @(); Style = 'Plain'
            Rows = 1; Symbol = '> '; Transient = $false; ShellIntegration = 'None'; IconSet = 'None'
        }
        try {
            Enable-Prompt -Configuration $configuration -Padding ':' -BlockSeparator:$false
            $text = (& prompt) -replace '\x1b\[[0-9;]*m'
            $text | Should -Match ':PS [0-9]'
            $text | Should -Match '^:PS '
            $text | Should -Match ':> '
        }
        finally { Disable-Prompt }
    }

    It 'pads both sides of statusline content' {
        $path = Join-Path $TestDrive 'spacing.txt'
        try {
            Enable-StatusLine -Block PowerShell -Target File -OutputPath $path -Style Plain -IconSet None -Padding ':' -BlockSeparator:$false -IdleTimeoutSeconds 0
            $deadline = [datetime]::UtcNow.AddSeconds(3)
            while (-not (Test-Path $path) -and [datetime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 25 }
            Get-Content -LiteralPath $path -Raw | Should -Match ':PS [0-9]'
        }
        finally { Disable-StatusLine }
    }

    It 'keeps an explicit emoji stopwatch intact with padding at the clipping boundary' {
        $stopwatch = [string][char]0x23f1 + [char]0xfe0f
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Block = @((New-TestBlock '12ms' -Icon $stopwatch -Padding ' ' -Background Blue -Alignment Left))
            Style = 'Powerline'; BlockSeparator = $true; Padding = ' '
            Rows = 1; Symbol = '> '; Transient = $false; ShellIntegration = 'None'
        }
        [StatusLine.Unicode]::Measure($stopwatch).Columns | Should -Be 2
        # Width 27 leaves 26 cells: 2 for '> ', 13 reserved for typing, and exactly 11 for the block.
        $sequence = Get-TestPrompt -Configuration $configuration -Width 27
        ($sequence -replace '\x1b\[[0-9;]*m') | Should -BeExactly " $stopwatch 12ms > "
    }

    It 'rounds the outer prompt edges with BlockSeparator=<Separated>' -ForEach @(
        @{ Separated = $false }; @{ Separated = $true }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Block = @((New-TestBlock 'L' -Background Red -Alignment Left), (New-TestBlock 'R' -Background Blue -Alignment Right))
            Style = 'Powerline'; BlockSeparator = $Separated; Padding = ' '
            Rows = 2; Symbol = '> '; ContextSymbol = ''; InputLinePrefix = ''; Transient = $false; ShellIntegration = 'None'
        }
        $sequence = Get-TestPrompt -Configuration $configuration -Width 40
        $contextRow = (($sequence -replace '\x1b\[[0-9;]*m') -split "`n")[0]
        $contextRow | Should -Match '^ L '
        $contextRow | Should -Match ' R $'
        $contextRow.Length | Should -Be 39
    }

    It 'uses an ordinary leading space and retains thin trailing padding for Duration' {
        $configuration = New-TestPromptConfiguration @{
            Left = @('Duration'); Right = @(); Block = @(); Input = @()
            Style = 'Powerline'; BlockSeparator = $false; Padding = [string][char]0x2009
            IconSet = 'None'; DurationWarning = $false; Rows = 1; Symbol = '> '; Transient = $false; ShellIntegration = 'None'
        }
        $sequence = Get-TestPrompt -Configuration $configuration -Duration ([timespan]::FromMilliseconds(9)) -ExitCode 0 -Width 30
        ($sequence -replace '\x1b\[[0-9;]*m') | Should -BeExactly " took 9ms$([char]0x2009)> "
    }
}