BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Terminal-specific command boundaries' -Tag Lifecycle {
    It 'reports optional terminal progress outside command output (<Protocol>, <Enabled>)' -ForEach @(
        @{ Protocol = 'Osc133'; Enabled = $true; Number = 133 }
        @{ Protocol = 'Osc633'; Enabled = $true; Number = 633 }
        @{ Protocol = 'None'; Enabled = $true; Number = 0 }
        @{ Protocol = 'Osc133'; Enabled = $false; Number = 133 }
    ) {
        $env:TERM_PROGRAM = 'unknown-test-terminal'
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); ShellIntegration = $Protocol
            ShellContext = $false; Transient = $true; TerminalProgress = $false
        }
        $pipelineOutput = @(Enable-Prompt -Configuration $configuration -TerminalProgress:$Enabled)
        $pipelineOutput.Count | Should -Be 0
        [StatusLine.Prompt]::Configuration.TerminalProgress | Should -Be $Enabled
        $runspace = [runspace]::DefaultRunspace
        $location = $ExecutionContext.SessionState.Path.CurrentLocation
        $originalOutput = [Console]::Out
        $writer = [IO.StringWriter]::new()
        try {
            [Console]::SetOut($writer)
            $null = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            [StatusLine.PromptSession]::CommandAccepted($runspace, ' ')
            $writer.ToString() | Should -BeExactly ''
            [StatusLine.PromptSession]::BeginTransient($runspace)
            $compact = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            [StatusLine.PromptSession]::EndTransient($runspace)
            $compact | Should -Not -Match '\x1b\]9;4;'
            $pipelineOutput = @([StatusLine.PromptSession]::CommandAccepted($runspace, 'Write-Output RESULT'))
            $pipelineOutput.Count | Should -Be 0
            $start = $writer.ToString()
            $finished = [StatusLine.PromptSession]::Render($runspace, $false, 7, $location, $null, 0)
            if ($Enabled) {
                $start.Contains("`e]9;4;3;50`e\") | Should -BeTrue
                $finished.Contains("`e]9;4;0;50`e\") | Should -BeTrue
                if ($Number) {
                    $start.Contains("`e]9;4;3;50`e\`e]$Number;C`a") | Should -BeTrue
                    $finished.Contains("`e]$Number;D;1`a`e]9;4;0;50`e\`e]$Number;A`a") | Should -BeTrue
                }
            }
            else {
                ($start + $finished) | Should -Not -Match '\x1b\]9;4;'
            }
        }
        finally {
            [StatusLine.PromptSession]::EndTransient($runspace)
            [Console]::SetOut($originalOutput)
            $writer.Dispose()
        }
    }

    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach {
        Disable-Prompt
        Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment
    }

    It 'restores prompt and command cell classification during a Windows Terminal transient redraw with <Style>' -ForEach @(
        @{ Style = 'Plain' }; @{ Style = 'Powerline' }
    ) {
        $env:TERM_PROGRAM = 'WindowsTerminal'
        $env:WT_SESSION = 'test-shell-marks'
        $configuration = New-TestPromptConfiguration @{
            Left = @([StatusLine.BlockSnapshots]::Create('LEFT'))
            Right = @([StatusLine.BlockSnapshots]::Create('RIGHT'))
            Input = @(); Block = @(); Rows = 2
            Style = $Style; IconSet = 'None'; Padding = ''
            Symbol = '> '; TransientSymbol = 'tiny> '; ConnectRows = $false
            ShellIntegration = 'Auto'; ShellContext = $false
            Transient = $true; TransientBlock = @('Duration')
            StartColor = '#123456'; EndColor = '#ABCDEF'
        }
        Enable-Prompt -Configuration $configuration
        $runspace = [runspace]::DefaultRunspace
        $location = $ExecutionContext.SessionState.Path.CurrentLocation
        $originalOutput = [Console]::Out
        $writer = [IO.StringWriter]::new()
        try {
            [Console]::SetOut($writer)
            $normal = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            $normal | Should -Match '^\x1b\]133;A'
            $normal | Should -Match '\x1b\]9;9;'
            $normal | Should -Not -Match '\x1b\]133;D'
            $normal.LastIndexOf("$escape]133;B") | Should -BeGreaterThan $normal.LastIndexOf([char]10)
            [StatusLine.PromptSession]::BeginTransient($runspace)
            $compact = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            [StatusLine.PromptSession]::EndTransient($runspace)
            $compact | Should -Match '^\x1b\]133;A'
            $compact | Should -Match '\x1b\]133;B\x07$'
            $compact | Should -Not -Match '\x1b\]133;[CD]|\x1b\]9;9;'
            # Simulate the text PSReadLine repaints after obtaining the compact prompt.
            $writer.Write($compact)
            $writer.WriteLine('Write-Output RESULT')
            [StatusLine.PromptSession]::CommandAccepted($runspace, 'Write-Output RESULT')
            $writer.WriteLine('RESULT')
            $finished = [StatusLine.PromptSession]::Render($runspace, $false, 7, $location, $null, 0)
            $writer.Write($finished)
            $transcript = $writer.ToString()
            $transcript | Should -Match '(?s)\x1b\]133;A.*tiny> .*\x1b\]133;B\x07Write-Output RESULT\r?\n\x1b\]133;C\x07RESULT\r?\n\x1b\]133;D;1\x07\x1b\]133;A'
            $diagnostics = [StatusLine.TerminalControl]::GetShellIntegration().Enabled
            $diagnostics.CommandStart | Should -Be 'OSC 133;B'
            $diagnostics.OutputStart | Should -Be 'OSC 133;C'
            $diagnostics.CommandTextFormat | Should -Be 'None'
            $diagnostics.TransientRedrawMarks | Should -BeTrue
        }
        finally {
            [Console]::SetOut($originalOutput)
            $writer.Dispose()
            [StatusLine.PromptSession]::EndTransient($runspace)
        }
    }

    It 'closes blank acceptance without assigning a false success or failure code' {
        $env:TERM_PROGRAM = 'WindowsTerminal'
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Rows = 1; Symbol = '> '; TransientSymbol = '> '
            ShellIntegration = 'Auto'; ShellContext = $false; Transient = $true
        }
        Enable-Prompt -Configuration $configuration
        $runspace = [runspace]::DefaultRunspace
        $location = $ExecutionContext.SessionState.Path.CurrentLocation
        $null = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
        [StatusLine.PromptSession]::CommandAccepted($runspace, '')
        $next = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
        $next | Should -Match '^\x1b\]133;D\x07\x1b\]133;A'
        $next | Should -Not -Match '\x1b\]133;D;'
    }

    It 'keeps terminal extensions distinct for <TerminalName>' -ForEach @(
        @{ TerminalName = 'WindowsTerminal'; Number = 133; Directory = 'OSC 9;9'; Format = 'None'; Reflow = $false }
        @{ TerminalName = 'contour'; Number = 133; Directory = 'OSC 7'; Format = 'OSC 133;C;cmdline_url=<percent-encoded-command>'; Reflow = $true }
        @{ TerminalName = 'vscode'; Number = 633; Directory = 'OSC 633'; Format = 'OSC 633;E;<escaped-command>[;<nonce>]'; Reflow = $false }
        @{ TerminalName = 'iterm.app'; Number = 133; Directory = 'OSC 1337'; Format = 'None'; Reflow = $false }
        @{ TerminalName = 'wezterm'; Number = 133; Directory = 'OSC 7'; Format = 'None'; Reflow = $false }
        @{ TerminalName = 'ghostty'; Number = 133; Directory = 'OSC 7'; Format = 'None'; Reflow = $false }
    ) {
        $env:TERM_PROGRAM = $TerminalName
        $env:VSCODE_SHELL_INTEGRATION = $null
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            ShellIntegration = 'Auto'; ShellContext = $true
            Rows = 1; Symbol = '> '; TransientSymbol = '> '; Transient = $true
        }
        Enable-Prompt -Configuration $configuration
        $features = [StatusLine.TerminalControl]::GetShellIntegration().Enabled
        $features.WorkingDirectory | Should -Be $Directory
        $features.CommandTextFormat | Should -Be $Format
        $features.ReflowControl | Should -Be $Reflow
        $features.PromptStart | Should -Be "OSC $Number;A"
        $features.OutputStart | Should -Be "OSC $Number;C"
        [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
        $transient = & prompt
        $transient | Should -Match ("^\x1b\]" + $Number + ';A')
        $transient | Should -Match ('\x1b\]' + $Number + ';B\x07$')
        $transient | Should -Not -Match 'cmdline_url|CurrentDir|Cwd=|\x1b\]3008;'
        [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
    }
}
