BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Terminal sequences' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'sanitizes window titles' {
        [StatusLine.TerminalControl]::TitleSequence("a`nb$escape") | Should -BeExactly "$escape]2;a b $escape\"
    }
}

Describe 'Shell markers and capabilities' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'emits the selected shell protocol <Protocol>' -ForEach @(
        @{ Protocol = 'Osc133'; Number = 133 }
        @{ Protocol = 'Osc633'; Number = 633 }
        @{ Protocol = 'ITerm2'; Number = 133 }
    ) {
        $configuration = (New-TestPromptConfiguration @{Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Left = @(); Right = @(); ShellIntegration = $Protocol })
        $text = Get-TestPrompt -Configuration $configuration -ExitCode 7 -IncludeIntegration
        $text.Contains("$escape]$Number;D;7`a") | Should -BeTrue
        $text.Contains("$escape]$Number;A`a") | Should -BeTrue
        $text.EndsWith("$escape]$Number;B`a", [StringComparison]::Ordinal) | Should -BeTrue
    }

    It 'disables integration explicitly' {
        $configuration = (New-TestPromptConfiguration @{Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Left = @(); Right = @(); ShellIntegration = 'None' })
        Get-TestPrompt -Configuration $configuration -IncludeIntegration | Should -Not -Match '\x1b\]'
    }

    It 'uses a conservative environment capability fallback' {
        $previous = $env:TERM_PROGRAM
        try {
            $env:TERM_PROGRAM = 'mintty'
            (Get-TestTerminalCapability).HostWritableStatusLine | Should -BeTrue
            $env:TERM_PROGRAM = 'WindowsTerminal'
            (Get-TestTerminalCapability).HostWritableStatusLine | Should -BeFalse
        }
        finally { $env:TERM_PROGRAM = $previous }
    }

    It 'accepts a validated status-display response for an unknown terminal' {
        $previous = $env:TERM_PROGRAM
        try {
            $env:TERM_PROGRAM = [guid]::NewGuid().ToString()
            $capability = Get-TestTerminalCapability -Response "$escape`P1`$r1`$~$escape\"
            $capability.HostWritableStatusLine | Should -BeTrue
            $capability.Source | Should -Be 'DECRQSS response'
        }
        finally { $env:TERM_PROGRAM = $previous }
    }

    It 'rejects unrelated terminal responses' {
        { Get-TestTerminalCapability -Response "$escape`P1`$r0m$escape\" } | Should -Throw
    }

    It 'returns a query without reading or writing console input' {
        Get-TestTerminalCapability -AsEscapeSequence | Should -BeExactly "$escape`P`$q`$~$escape\"
    }
}

Describe 'Directory display and terminal-specific integration' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    BeforeEach {
        $directoryRoot = (Get-Item -LiteralPath TestDrive:\).FullName
        $savedStyle = [StatusLine.DirectoryDisplay]::Style
        $savedBase = [StatusLine.DirectoryDisplay]::BasePath
        $savedMaximum = [StatusLine.DirectoryDisplay]::MaximumCharacters
        $savedTerminal = $env:TERM_PROGRAM
        $savedWtSession = $env:WT_SESSION
        $env:WT_SESSION = $null
    }

    AfterEach {
        [StatusLine.DirectoryDisplay]::Style = $savedStyle
        [StatusLine.DirectoryDisplay]::BasePath = $savedBase
        [StatusLine.DirectoryDisplay]::MaximumCharacters = $savedMaximum
        $env:TERM_PROGRAM = $savedTerminal
        $env:WT_SESSION = $savedWtSession
    }

    It 'reports cwd using each terminal feature rather than the OSC marker number' -ForEach @(
        @{ Terminal = 'contour'; Protocol = 'Osc133'; Prefix = "`e]7;"; Encoded = $true }
        @{ Terminal = 'wezterm'; Protocol = 'Osc133'; Prefix = "`e]7;"; Encoded = $true }
        @{ Terminal = 'WindowsTerminal'; Protocol = 'Osc133'; Prefix = "`e]9;9;"; Encoded = $false }
        @{ Terminal = 'vscode'; Protocol = 'Osc633'; Prefix = "`e]633;P;Cwd="; Encoded = $false }
    ) {
        $env:TERM_PROGRAM = $Terminal
        $path = [IO.Directory]::CreateDirectory((Join-Path $directoryRoot 'space % folder')).FullName
        Push-Location -LiteralPath $path
        try {
            $configuration = (New-TestPromptConfiguration @{ Left = @(); Right = @(); ShellIntegration = $Protocol })
            $sequence = Get-TestPrompt -Configuration $configuration -IncludeIntegration
            $sequence.Contains($Prefix) | Should -BeTrue
            $sequence.Contains('%20') | Should -Be $Encoded
            if ($Terminal -eq 'vscode' -and $IsWindows) { $sequence.Contains('\x5c') | Should -BeTrue }
        }
        finally { Pop-Location }
    }

    It 'does not infer cwd support for an unknown OSC 133 terminal' {
        $env:TERM_PROGRAM = 'unknown-test-terminal'
        $configuration = (New-TestPromptConfiguration @{ Left = @(); Right = @(); ShellIntegration = 'Osc133' })
        $sequence = Get-TestPrompt -Configuration $configuration -IncludeIntegration
        $sequence.Contains("`e]133;A") | Should -BeTrue
        $sequence.Contains("`e]7;") | Should -BeFalse
    }

    It 'detects Windows Terminal from WT_SESSION with missing or unrecognized TERM_PROGRAM' -ForEach @(
        @{ ProgramName = '' }; @{ ProgramName = '   ' }; @{ ProgramName = 'unrecognized-shell-value' }
    ) {
        $env:TERM_PROGRAM = $ProgramName
        $env:WT_SESSION = 'test-windows-terminal-session'
        $capability = Get-TestTerminalCapability
        $capability.Name | Should -BeExactly 'WindowsTerminal'
        $capability.Integration | Should -Be Osc133
        $capability.Source | Should -BeExactly 'WT_SESSION'
    }

    It 'preserves a recognized terminal despite inherited WT_SESSION' -ForEach @(
        @{ ProgramName = 'vscode'; Expected = 'Osc633' }; @{ ProgramName = 'contour'; Expected = 'Osc133' }
    ) {
        $env:TERM_PROGRAM = $ProgramName
        $env:WT_SESSION = 'inherited-session'
        $capability = Get-TestTerminalCapability
        $capability.Name | Should -BeExactly $ProgramName
        $capability.Integration | Should -Be $Expected
    }

    It 'recognizes Windows Terminal release-channel names from the reference profile' -ForEach @(
        @{ Channel = 'Stable' }; @{ Channel = 'Preview' }; @{ Channel = 'Canary' }; @{ Channel = 'Dev' }
    ) {
        $env:TERM_PROGRAM = "WindowsTerminal_$Channel"
        $capability = Get-TestTerminalCapability
        $capability.Name | Should -BeExactly 'WindowsTerminal'
        $capability.Integration | Should -Be Osc133
        $configuration = (New-TestPromptConfiguration @{ Left = @(); Right = @(); ShellIntegration = 'Auto' })
        $sequence = Get-TestPrompt -Configuration $configuration -IncludeIntegration
        $sequence.Contains("`e]133;A") | Should -BeTrue
        $sequence.Contains("`e]133;B") | Should -BeTrue
        $sequence.Contains("`e]9;9;") | Should -BeTrue
    }
}

Describe 'VT parser and Unicode width regressions' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'accepts C1-framed status display replies' {
        (Get-TestTerminalCapability -Response "$([char]0x90)1`$r2`$~$([char]0x9c)").HostWritableStatusLine | Should -BeTrue
    }
}

Describe 'Shell integration metadata regressions' -Tag Lifecycle {
    BeforeEach {
        $savedTerminalEnvironment = Enter-TestTerminalEnvironment
        $bell = [char]7
    }
    AfterEach {
        Disable-Prompt
        Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment
    }

    It 'keeps Windows Terminal integration when a VS Code flag was inherited' {
        $env:TERM_PROGRAM = 'WindowsTerminal'
        $env:WT_SESSION = 'test-session'
        $env:VSCODE_SHELL_INTEGRATION = '1'
        $options = @{
            Left = @(); Right = @(); Input = @()
            ShellIntegration = 'Auto'; Transient = $false
            Symbol = '> '; ContextSymbol = ''; InputLinePrefix = ''
            Rows = 1; Padding = ''; Style = 'Plain'
        }
        Enable-Prompt -Configuration (New-TestPromptConfiguration $options)
        $text = & prompt
        $path = $ExecutionContext.SessionState.Path.CurrentLocation.ProviderPath
        $text.Contains("$escape]133;A") | Should -BeTrue
        $text.Contains("$escape]9;9;" + [char]34 + $path + [char]34 + $bell) | Should -BeTrue
    }

    It 'reports VS Code platform and cwd within the prompt boundary' {
        $env:TERM_PROGRAM = 'vscode'
        $configuration = New-TestPromptConfiguration @{
            ShellIntegration = 'Osc633'; Transient = $false
            Left = @(); Right = @(); Input = @()
            Rows = 1; Padding = ''; Symbol = '> '
        }
        $text = Get-TestPrompt -Configuration $configuration -IncludeIntegration
        $start = $text.IndexOf("$escape]633;A$bell", [StringComparison]::Ordinal)
        $platform = $text.IndexOf("$escape]633;P;IsWindows=$IsWindows$bell", [StringComparison]::Ordinal)
        $directory = $text.IndexOf("$escape]633;P;Cwd=", [StringComparison]::Ordinal)
        $start | Should -BeGreaterOrEqual 0
        $platform | Should -BeGreaterThan $start
        $directory | Should -BeGreaterThan $platform
    }

    It 'includes escaped command text and the VS Code nonce where supported' {
        $env:TERM_PROGRAM = 'vscode'
        $env:VSCODE_NONCE = 'test-nonce'
        $markers = [StatusLine.TerminalControl].Assembly.GetType('StatusLine.ShellMarkers')
        $execute = $markers.GetMethod('Execute', [Reflection.BindingFlags]'Static,Public')
        $profiles = [StatusLine.TerminalControl].Assembly.GetType('StatusLine.ShellIntegrationProfile')
        $profile = $profiles.GetMethod('Resolve').Invoke($null, @([StatusLine.ShellIntegration]::Osc633))
        $actual = $execute.Invoke($null, @($profile, 'echo a;b', $false))
        $nonce = if (!$IsWindows -or [OperatingSystem]::IsWindowsVersionAtLeast(10, 0, 22000)) { ';test-nonce' } else { '' }
        $actual | Should -BeExactly "$escape]633;E;echo\x20a\x3bb$nonce$bell$escape]633;C$bell"
    }

    It 'identifies the local host in OSC 7 and preserves encoded path components' -ForEach @(
        @{ TerminalName = 'wezterm' }; @{ TerminalName = 'ghostty' }
    ) {
        $env:TERM_PROGRAM = $TerminalName
        $path = [IO.Directory]::CreateDirectory((Join-Path $TestDrive 'cwd space %')).FullName
        Push-Location -LiteralPath $path
        try {
            $configuration = New-TestPromptConfiguration @{
                ShellIntegration = 'Osc133'; Transient = $false
                Left = @(); Right = @(); Input = @()
                Rows = 1; Padding = ''; Symbol = '> '
            }
            $text = Get-TestPrompt -Configuration $configuration -IncludeIntegration
            $text.Contains("$escape]7;file://$([System.Net.Dns]::GetHostName().ToLowerInvariant())/") | Should -BeTrue
            $text.Contains('cwd%20space%20%25') | Should -BeTrue
        }
        finally { Pop-Location }
    }
}
