BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'New configuration behavior' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }
    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'detects <ProgramName> without assuming host-writable status support' -ForEach @(
        @{ ProgramName = 'ghostty'; Protocol = 'Osc133' }
        @{ ProgramName = 'rio'; Protocol = 'Osc133' }
        @{ ProgramName = 'foot'; Protocol = 'Osc133' }
        @{ ProgramName = 'mlterm'; Protocol = 'None' }
        @{ ProgramName = 'mintty'; Protocol = 'None' }
    ) {
        $env:TERM_PROGRAM = $ProgramName
        $capability = Get-TestTerminalCapability
        $capability.Integration | Should -Be $Protocol
        $capability.HostWritableStatusLine | Should -Be ($ProgramName -eq 'mintty')
    }

    It 'emits terminal palette colors for unthemed built-in blocks' {
        $block = New-TestBlock -Kind Clock -IconSet None -Padding ''
        $block.Style | Should -Match '\x1b\[0;37;49m'
        $block.Style | Should -Not -Match '38;2;'
    }

    It 'rejects unknown Color objects as well as color strings' {
        $invalid = [Drawing.Color]::FromName('TerminalBlue')
        { [StatusLine.BlockSnapshots]::Create('x', $invalid, $null) } | Should -Throw '*Unknown drawing color*'
        { [StatusLine.TerminalColor]::new($invalid) } | Should -Throw '*Unknown drawing color*'
        { [StatusLine.TerminalColor]::new([ConsoleColor]99) } | Should -Throw
    }

    It 'maps all console colors to their ANSI foreground and background codes' {
        $ansiIndices = @(0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15)
        foreach ($palette in [Enum]::GetValues([ConsoleColor])) {
            $index = $ansiIndices[[int]$palette]
            $foreground = 30 + ($index -band 7) + $(if ($index -ge 8) { 60 } else { 0 })
            $background = $foreground + 10
            $block = [StatusLine.BlockSnapshots]::Create('x', $palette, $palette)
            $block.Style | Should -BeExactly "$([char]27)[0;$foreground;${background}m"
            $block.Foreground.Palette | Should -Be $palette
            { $block.Foreground.get_Rgb() } | Should -Throw '*no known RGB*'
        }
    }

    It 'distinguishes terminal defaults, transparent RGB, and invalid named colors' {
        $defaults = [StatusLine.BlockSnapshots]::Create('x', $null, $null)
        $defaults.Foreground.IsEmpty | Should -BeTrue
        $defaults.Style | Should -BeExactly "$([char]27)[0;39;49m"
        $transparent = [StatusLine.BlockSnapshots]::Create('x', [Drawing.Color]::FromArgb(0, 0, 0, 0), $null)
        $transparent.Foreground.IsEmpty | Should -BeFalse
        $transparent.Style | Should -BeExactly "$([char]27)[0;38;2;0;0;0;49m"
        { [StatusLine.BlockSnapshots]::Create('x', [Drawing.Color]::FromName('Unknown'), $null) } | Should -Throw
    }
    It 'accepts hex representations without an exact string-length restriction' -ForEach @(
        @{ Hex = '#0400fdff'; Red = 4; Green = 0; Blue = 253 }
        @{ Hex = '#ff7b00ff'; Red = 255; Green = 123; Blue = 0 }
        @{ Hex = '#123'; Red = 17; Green = 34; Blue = 51 }
        @{ Hex = '#1234'; Red = 17; Green = 34; Blue = 51 }
        @{ Hex = '0x123456'; Red = 18; Green = 52; Blue = 86 }
        @{ Hex = '123456'; Red = 18; Green = 52; Blue = 86 }
        @{ Hex = '#1234567890'; Red = 18; Green = 52; Blue = 86 }
    ) {
        $color = [StatusLine.BlockSnapshots]::Create('x', $Hex, $null).Foreground.Rgb
        $color.R | Should -Be $Red
        $color.G | Should -Be $Green
        $color.B | Should -Be $Blue
    }

    It 'binds RGBA strings as drawing colors for both gradient cmdlets' {
        $converter = [StatusLine.ColorTransformAttribute]::new()
        $converter.Transform($ExecutionContext, '#0400fdff').GetType() | Should -Be ([Drawing.Color])
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); ShellIntegration = 'None'; Transient = $false
        }
        try {
            Enable-Prompt -Configuration $configuration -StartColor '#0400fdff' -EndColor '#ff7b00ff'
            Enable-StatusLine -Left Clock -Center @() -Right @() -Target File -OutputPath "$TestDrive/gradient.txt" -StartColor '#0400fdff' -EndColor '#ff7b00ff' -Style Plain -IconSet None -Padding '' -BlockSeparator:$false -IdleTimeoutSeconds 0 -IntervalMilliseconds 1000
        }
        finally {
            Disable-StatusLine
            Disable-Prompt
        }
    }

    It 'retains a typed console palette through script definition parameter binding' {
        try {
            $block = New-StatusLineBlock -Name PaletteProbe -ScriptBlock { 'x' } -Foreground ([ConsoleColor]::Cyan)
            $block.Foreground.Palette | Should -Be ([ConsoleColor]::Cyan)
        }
        finally { $block.Dispose() }
    }
    It 'completes color names with wildcard matching' {
        $completionText = '$sample = New-StatusLineBlock -Name sample -ScriptBlock { 1 } -Foreground *Blue'
        $completion = [Management.Automation.CommandCompletion]::CompleteInput($completionText, $completionText.Length, $null)
        $completion.CompletionMatches.CompletionText | Should -Contain 'MidnightBlue'
        $completion.CompletionMatches.CompletionText | Should -Not -Contain 'BlueViolet'
    }

    It 'escapes weather locations without changing the service or query' {
        $type = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.Blocks.BuiltIns.Machine.WeatherBlock')
        $method = $type.GetMethod('CreateUri', [Reflection.BindingFlags]'NonPublic,Static')
        $uri = $method.Invoke($null, @('Stockholm/Sweden?format=bad'))
        $uri.Host | Should -BeExactly 'wttr.in'
        $uri.Query | Should -BeExactly '?format=%25c%25t'
        $uri.AbsoluteUri | Should -Match 'Stockholm%2FSweden%3Fformat%3Dbad'
        $method.Invoke($null, @($null)).AbsoluteUri | Should -BeExactly 'https://wttr.in/?format=%25c%25t'
    }

    It 'keeps boxes separate when background blending is explicitly disabled' {
        $blocks = @(New-TestBlock 'L' -Foreground Blue; New-TestBlock 'R' -Foreground Red -Alignment Right)
        $sequence = Write-TestStatusLine -Block $blocks -Width 7 -Style Powerline -BlendBackground:$false -AsEscapeSequence
        $sequence | Should -Not -Match '48;2;128;0;128m'
    }
}
Describe 'Directory depth parameters' -Tag Contract {
    BeforeEach { $savedDepth = [StatusLine.DirectoryDisplay]::MaximumComponents }
    AfterEach {
        Disable-Prompt
        Disable-StatusLine
        [StatusLine.DirectoryDisplay]::MaximumComponents = $savedDepth
    }

    It 'sets and retains the shared depth through <Command>' -ForEach @(
        @{ Command = 'Enable-Prompt' }
        @{ Command = 'Enable-StatusLine' }
    ) {
        $parameters = if ($Command -eq 'Enable-Prompt') {
            @{ Configuration = (New-TestPromptConfiguration @{ Left = @('Directory'); Right = @(); Transient = $false; ShellIntegration = 'None' }) }
        }
        else {
            @{ Block = @('Directory'); Target = 'File'; OutputPath = (Join-Path $TestDrive 'depth.txt'); IntervalMilliseconds = 1000; IdleTimeoutSeconds = 0 }
        }
        [StatusLine.DirectoryDisplay]::MaximumComponents = 5
        & $Command @parameters -DirectoryDepth 2
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be 2
        & $Command @parameters
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be 2
        & $Command @parameters -DirectoryDepth 0
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be 0
        { & $Command @parameters -DirectoryDepth -1 } | Should -Throw
    }
}