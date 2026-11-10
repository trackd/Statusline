BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    Import-Module PSReadLine
}

Describe 'Terminal-specific command boundaries' -Tag Lifecycle {
    It 'keeps live Windows Terminal marks when no acceptance hook can be installed' {
        $env:TERM_PROGRAM = 'WindowsTerminal'
        $previousReader = $function:global:PSConsoleHostReadLine
        try {
            Remove-Module PSReadLine
            $function:global:PSConsoleHostReadLine = $null
            Enable-Prompt -Left @() -Right @() -Transient -ShellIntegration Osc133
            $rendered = & prompt
            $rendered | Should -Match '\x1b\]133;A'
            $rendered | Should -Match '\x1b\]133;B'
        }
        finally {
            Disable-Prompt
            Import-Module PSReadLine
            $function:global:PSConsoleHostReadLine = $previousReader
        }
    }

    It 'uses the completion code for symbol color when history has no new entry' {
        Enable-Prompt -Left @() -Right @() -Symbol '>> ' -ColorSymbolByExitCode -ShellIntegration None
        $runspace = [runspace]::DefaultRunspace
        $location = $ExecutionContext.SessionState.Path.CurrentLocation
        [StatusLine.PromptSession]::CommandAccepted($runspace, 'Write-Error failed')
        $rendered = [StatusLine.PromptSession]::Render($runspace, $false, $null, $location, $null, 0)
        $rendered | Should -Match '\x1b\[0;91;49m>> '
    }

    It 'restores multiline accepted input without replaying control sequences after a repaint failure' {
        Enable-Prompt -Left @() -Right @() -Transient -TransientSymbol 'tiny> ' -ShellIntegration None
        $runspace = [runspace]::DefaultRunspace
        $line = "if (`$true) {`n    Write-Output 'Malmö'`e[2J`n}"
        [StatusLine.PromptSession]::TryBeginTransient($runspace, $line) | Should -BeTrue
        try {
            $recovery = [StatusLine.PromptSession]::GetRepaintRecovery($runspace, $line, '>> ')
            $recovery | Should -BeExactly "tiny> if (`$true) {`r`n>>     Write-Output 'Malmö' `r`n>> }`r`n`e[?25h"
        }
        finally { [StatusLine.PromptSession]::EndTransient($runspace) }
    }
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
            $normal | Should -Not -Match '\x1b\]133;'
            $normal | Should -Match '\x1b\]9;9;'
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
            $transcript | Should -Match '(?s)\x1b\]133;A.*tiny> .*\x1b\]133;B\x07Write-Output RESULT\r?\n\x1b\]133;C\x07RESULT\r?\n\x1b\]133;D;1\x07'
            $finished | Should -Not -Match '\x1b\]133;[AB]'
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

    It 'emits no Windows Terminal scrollbar marks for blank input and marks a command without a redraw' {
        $env:TERM_PROGRAM = 'WindowsTerminal'
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @()
            Rows = 1; Symbol = '> '; TransientSymbol = '> '
            ShellIntegration = 'Auto'; ShellContext = $false; Transient = $true
        }
        Enable-Prompt -Configuration $configuration
        $runspace = [runspace]::DefaultRunspace
        $location = $ExecutionContext.SessionState.Path.CurrentLocation
        $originalOutput = [Console]::Out
        $writer = [IO.StringWriter]::new()
        try {
            [Console]::SetOut($writer)
            $live = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            [StatusLine.PromptSession]::TryBeginTransient($runspace, '') | Should -BeTrue
            $compact = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            [StatusLine.PromptSession]::EndTransient($runspace)
            [StatusLine.PromptSession]::CommandAccepted($runspace, '')
            $next = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            ($live + $compact + $writer.ToString() + $next) | Should -Not -Match '\x1b\]133;'

            [StatusLine.PromptSession]::CommandAccepted($runspace, 'Write-Output RESULT')
            $writer.ToString() | Should -BeExactly "`e]133;A`a`e]133;B`a`e]133;C`a"
            $finished = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 0)
            $finished | Should -Match '^\x1b\]133;D;0\x07'
        }
        finally {
            [Console]::SetOut($originalOutput)
            $writer.Dispose()
        }
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
