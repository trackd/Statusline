BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Refresh during command execution' -Tag Lifecycle {
    BeforeEach {
        $savedTerminalEnvironment = Enter-TestTerminalEnvironment
        $oldReadLine = $function:global:PSConsoleHostReadLine
        $function:global:PSConsoleHostReadLine = { 'Start-Sleep -Seconds 2' }
    }

    AfterEach {
        Disable-Prompt
        Disable-StatusLine -Force
        $function:global:PSConsoleHostReadLine = $oldReadLine
        Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment
    }

    It 'updates <Target> during a running command with UpdateDuringCommand=<DuringCommand>, Prompt=<WithPrompt>' -ForEach @(
        @{ Target = 'HostWritable'; DuringCommand = $false; ExpectedChange = $false; WithPrompt = $false }
        @{ Target = 'HostWritable'; DuringCommand = $true; ExpectedChange = $true; WithPrompt = $false }
        @{ Target = 'File'; DuringCommand = $false; ExpectedChange = $true; WithPrompt = $false }
        @{ Target = 'File'; DuringCommand = $true; ExpectedChange = $true; WithPrompt = $false }
        @{ Target = 'HostWritable'; DuringCommand = $false; ExpectedChange = $false; WithPrompt = $true }
        @{ Target = 'HostWritable'; DuringCommand = $true; ExpectedChange = $true; WithPrompt = $true }
    ) {
        $writer = [IO.StringWriter]::new()
        $originalOutput = [Console]::Out
        $path = Join-Path $TestDrive 'command-status'
        $ownerId = [runspace]::DefaultRunspace.InstanceId
        $commandClock = New-StatusLineBlock -Name commandClock -ScriptBlock { Get-Date -Format 'HH:mm:ss.fffffff' } -IntervalMilliseconds 250
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            $options = @{
                Block = $commandClock; Target = $Target; OutputPath = $path
                Force = $true; UpdateDuringCommand = $DuringCommand
                IntervalMilliseconds = 100; IdleTimeoutSeconds = 0
                Style = 'Plain'; Padding = ''; IconSet = 'None'
                BlockSeparator = $false; BlendBackground = $false
            }
            if ($WithPrompt) {
                $configuration = New-TestPromptConfiguration @{
                    Left = @(); Right = @(); Input = @()
                    ShellIntegration = 'None'; ShellContext = $false
                    Transient = $false; Style = 'Plain'; Padding = ''
                }
                Enable-Prompt -Configuration $configuration
            }
            Enable-StatusLine @options
            if ($Target -eq 'HostWritable') {
                Set-TestHostDeferral -Deferred
            }
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('[2K') -or (Test-Path -LiteralPath $path) }
            $command = PSConsoleHostReadLine
            $command | Should -BeExactly 'Start-Sleep -Seconds 2'
            $before = if ($Target -eq 'File') { Get-Content -LiteralPath $path -Raw } else { $writer.ToString() }
            # Execute on the foreground runspace, without OnIdle or an explicit flush.
            & ([scriptblock]::Create($command))
            $after = if ($Target -eq 'File') { Get-Content -LiteralPath $path -Raw } else { $writer.ToString() }
            ($after -cne $before) | Should -Be $ExpectedChange
            [StatusLine.StatusLineActivity]::PromptReady($ownerId)
            if ($Target -eq 'HostWritable') {
                $beforePrompt = $writer.ToString()
                [StatusLine.StatusLineActivity]::BeginReadLine($ownerId)
                [StatusLine.StatusLineActivity]::FlushHost($ownerId)
                Wait-RefreshCondition {
                    [StatusLine.StatusLineActivity]::FlushHost($ownerId)
                    $writer.ToString().Length -gt $beforePrompt.Length
                }
            }
        }
        finally {
            Disable-StatusLine -Force
            Disable-Prompt
            [Console]::SetOut($originalOutput)
            $commandClock.Dispose()
            $writer.Dispose()
        }
    }
}
