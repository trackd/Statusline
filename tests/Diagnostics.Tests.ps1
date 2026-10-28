BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Shell integration diagnostics' -Tag Contract {
    BeforeEach {
        $savedTerminalEnvironment = Enter-TestTerminalEnvironment
        Disable-Prompt
    }

    AfterEach {
        Disable-Prompt
        Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment
    }

    It 'distinguishes detection from an absent or disabled prompt' {
        $env:TERM_PROGRAM = 'contour'
        $diagnostics = [StatusLine.TerminalControl]::GetShellIntegration()
        $diagnostics.Detected.Integration | Should -Be 'Osc133'
        $diagnostics.Enabled.PromptInstalled | Should -BeFalse
        $diagnostics.Enabled.Protocol | Should -Be 'None'
        $diagnostics.Enabled.RequestedProtocol | Should -BeNullOrEmpty
        $diagnostics.Enabled.WorkingDirectory | Should -Be 'None'
        $configuration = New-TestPromptConfiguration @{
            ShellIntegration = 'None'; ShellContext = $false; Transient = $false
            Left = @(); Right = @(); Input = @(); Style = 'Plain'
        }
        Enable-Prompt -Configuration $configuration
        $enabled = [StatusLine.TerminalControl]::GetShellIntegration().Enabled
        $enabled.PromptInstalled | Should -BeTrue
        $enabled.RequestedProtocol | Should -Be 'None'
        $enabled.CommandBoundaries | Should -BeFalse
        Disable-Prompt
        [StatusLine.TerminalControl]::GetShellIntegration().Enabled.PromptInstalled | Should -BeFalse
    }

    It 'reports active features for <TerminalName> with <Requested>' -ForEach @(
        @{
            TerminalName = 'contour'; Requested = 'Auto'; Context = $true; Existing = ''
            Expected = 'Osc133'; Directory = 'OSC 7'; Command = $true; Reflow = $true
            Metadata = $true
        }
        @{
            TerminalName = 'contour'; Requested = 'Osc133'; Context = $false; Existing = ''
            Expected = 'Osc133'; Directory = 'OSC 7'; Command = $true; Reflow = $true
            Metadata = $false
        }
        @{
            TerminalName = 'WindowsTerminal'; Requested = 'Auto'; Context = $true; Existing = ''
            Expected = 'Osc133'; Directory = 'OSC 9;9'; Command = $false; Reflow = $false
            Metadata = $false
        }
        @{
            TerminalName = 'vscode'; Requested = 'Auto'; Context = $true; Existing = '1'
            Expected = 'None'; Directory = 'None'; Command = $false; Reflow = $false
            Metadata = $false
        }
        @{
            TerminalName = 'vscode'; Requested = 'Osc633'; Context = $false; Existing = '1'
            Expected = 'Osc633'; Directory = 'OSC 633'; Command = $true; Reflow = $false
            Metadata = $false
        }
        @{
            TerminalName = 'iterm.app'; Requested = 'Auto'; Context = $false; Existing = ''
            Expected = 'ITerm2'; Directory = 'OSC 1337'; Command = $false; Reflow = $false
            Metadata = $false
        }
    ) {
        $env:TERM_PROGRAM = $TerminalName
        $env:VSCODE_SHELL_INTEGRATION = $Existing
        $configuration = New-TestPromptConfiguration @{
            ShellIntegration = $Requested; ShellContext = $Context; Transient = $false
            Left = @(); Right = @(); Input = @(); Style = 'Plain'
        }
        Enable-Prompt -Configuration $configuration
        $diagnostics = [StatusLine.TerminalControl]::GetShellIntegration()
        $diagnostics.Detected.Name | Should -Be $TerminalName
        $enabled = $diagnostics.Enabled
        $enabled.PromptInstalled | Should -BeTrue
        $enabled.RequestedProtocol | Should -Be $Requested
        $enabled.Protocol | Should -Be $Expected
        $enabled.CommandBoundaries | Should -Be ($Expected -ne 'None')
        $enabled.WorkingDirectory | Should -Be $Directory
        $enabled.CommandText | Should -Be $Command
        $enabled.ReflowControl | Should -Be $Reflow
        $enabled.ShellContext | Should -Be $Metadata

        $modulePath = (Get-Module StatusLine).Path
        $otherShell = [powershell]::Create()
        try {
            $null = $otherShell.AddScript({
                param($modulePath)
                Import-Module $modulePath
                [StatusLine.TerminalControl]::GetShellIntegration().Enabled.PromptInstalled
            }).AddArgument($modulePath)
            $otherShell.Invoke()[0] | Should -BeFalse
        }
        finally {
            $otherShell.Dispose()
        }
    }
}
