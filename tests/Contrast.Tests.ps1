BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Filled block text contrast' -Tag Rendering {
    It 'uses the terminal default foreground on an unknown palette fill' {
        $block = [StatusLine.BlockSnapshots]::Create('TEXT', [ConsoleColor]::Blue, $null, 'Left', '')
        $options = [StatusLine.BlockRenderOptions]@{
            Width = 30; Style = 'Diamond'; BlockSeparator = $false; TextContrast = 'Automatic'
        }
        [StatusLine.TerminalControl]::BlocksSequence(@($block), $options) |
            Should -Match '39;104mTEXT'
    }

    It 'preserves an explicit text foreground on an unknown palette background' {
        $block = [StatusLine.BlockSnapshots]::Create('TEXT', '#123456', [ConsoleColor]::Blue, 'Left', '')
        $options = [StatusLine.BlockRenderOptions]@{
            Width = 30; Style = 'Diamond'; BlockSeparator = $false; TextContrast = 'Automatic'
        }
        [StatusLine.TerminalControl]::BlocksSequence(@($block), $options) |
            Should -Match '38;2;18;52;86;104mTEXT'
    }

    It 'chooses <Expected> text on <Background>' -ForEach @(
        @{ Background = '#ffffff'; Expected = '0;0;0' }
        @{ Background = '#ffff00'; Expected = '0;0;0' }
        @{ Background = '#ff7b00'; Expected = '0;0;0' }
        @{ Background = '#808080'; Expected = '0;0;0' }
        @{ Background = '#000000'; Expected = '255;255;255' }
        @{ Background = '#0400fd'; Expected = '255;255;255' }
    ) {
        $block = [StatusLine.BlockSnapshots]::Create('TEXT', '#123456', $Background, 'Left', '')
        $options = [StatusLine.BlockRenderOptions]@{
            Width = 30; Style = 'Diamond'; BlockSeparator = $false; TextContrast = 'Automatic'
        }
        $sequence = [StatusLine.TerminalControl]::BlocksSequence(@($block), $options)
        $sequence | Should -Match "38;2;$Expected;48;2;[0-9;]+mTEXT"
    }

    It 'supports the explicit <Mode> policy' -ForEach @(
        @{ Mode = 'Black'; Expected = '0;0;0' }
        @{ Mode = 'White'; Expected = '255;255;255' }
        @{ Mode = 'Preserve'; Expected = '18;52;86' }
    ) {
        $block = [StatusLine.BlockSnapshots]::Create('TEXT', '#123456', '#ffffff', 'Left', '')
        $options = [StatusLine.BlockRenderOptions]@{
            Width = 30; Style = 'Diamond'; BlockSeparator = $false; TextContrast = $Mode
        }
        [StatusLine.TerminalControl]::BlocksSequence(@($block), $options) |
            Should -Match "38;2;$Expected;48;2;255;255;255mTEXT"
    }

    It 'selects contrast separately for each prompt gradient block' {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Block = @([StatusLine.BlockSnapshots]::Create('LEFT', $null, $null, 'Left', ''), [StatusLine.BlockSnapshots]::Create('RIGHT', $null, $null, 'Left', ''))
            Style = 'Diamond'; TextContrast = 'Automatic'; StartColor = '#000000'; EndColor = '#ffffff'
            Padding = ''; Rows = 1; Symbol = '> '; InputLinePrefix = ''; ContextSymbol = ''
            Transient = $false; ShellIntegration = 'None'
        }
        $sequence = Get-TestPrompt -Configuration $configuration -Width 80
        $sequence | Should -Match '38;2;255;255;255;48;2;0;0;0mLEFT'
        $sequence | Should -Match '38;2;0;0;0;48;2;255;255;255mRIGHT'
    }

    It 'forwards the prompt cmdlet policy over configuration' {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Block = @([StatusLine.BlockSnapshots]::Create('TEXT', '#123456', '#ffffff'))
            Style = 'Diamond'; TextContrast = 'Automatic'; Padding = ''
            Transient = $false; ShellIntegration = 'None'
        }
        try {
            Enable-Prompt -Configuration $configuration -TextContrast White
            (& prompt) | Should -Match '38;2;255;255;255;48;2;255;255;255mTEXT'
        }
        finally { Disable-Prompt }
    }

    It 'rejects an undefined contrast policy' {
        $options = [StatusLine.BlockRenderOptions]@{ Width = 20; Style = 'Diamond'; TextContrast = [Enum]::ToObject([StatusLine.TextContrastMode], 99) }
        { [StatusLine.TerminalControl]::BlocksSequence(@([StatusLine.BlockSnapshots]::Create('x')), $options) } | Should -Throw
    }

    It 'refreshes cached gradient colors when block count or endpoints change' {
        $options = [StatusLine.BlockRenderOptions]@{
            Width = 80; Style = 'Diamond'; BlockSeparator = $false; TextContrast = 'Automatic'
            StartColor = '#000000'; EndColor = '#ffffff'
        }
        $left = [StatusLine.BlockSnapshots]::Create('LEFT', $null, $null, 'Left', '')
        $right = [StatusLine.BlockSnapshots]::Create('RIGHT', $null, $null, 'Left', '')
        $middle = [StatusLine.BlockSnapshots]::Create('MIDDLE', $null, $null, 'Left', '')
        $first = [StatusLine.TerminalControl]::BlocksSequence(@($left, $right), $options)
        $first | Should -Match '38;2;0;0;0;48;2;255;255;255mRIGHT'
        $expanded = [StatusLine.TerminalControl]::BlocksSequence(@($left, $middle, $right), $options)
        $expanded | Should -Match '38;2;0;0;0;48;2;128;128;128mMIDDLE'
        [StatusLine.TerminalControl]::BlocksSequence(@($left, $right), $options) | Should -BeExactly $first
        $options.StartColor = '#ffffff'
        $options.EndColor = '#000000'
        $reversed = [StatusLine.TerminalControl]::BlocksSequence(@($left, $right), $options)
        $reversed | Should -Match '38;2;0;0;0;48;2;255;255;255mLEFT'
        $reversed | Should -Match '38;2;255;255;255;48;2;0;0;0mRIGHT'
    }
}
