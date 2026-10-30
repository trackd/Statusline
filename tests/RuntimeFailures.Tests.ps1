BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    if (-not ('FailingTerminalWriter' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'FailingTerminalWriter.cs')
    }
    if (-not ('BlockingReviewWriter' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'BlockingReviewWriter.cs')
    }
}

Describe 'Terminal failure isolation' -Tag Lifecycle {
    BeforeEach {
        $environment = Enter-TestTerminalEnvironment
        $originalPrompt = $function:global:prompt
        $originalReader = $function:global:PSConsoleHostReadLine
        $originalOutput = [Console]::Out
        $function:global:PSConsoleHostReadLine = { 'accepted-command' }
        $writer = [FailingTerminalWriter]::new()
    }
    AfterEach {
        [Console]::SetOut($originalOutput)
        Disable-Prompt
        Disable-StatusLine
        $function:global:prompt = $originalPrompt
        $function:global:PSConsoleHostReadLine = $originalReader
        Restore-TestTerminalEnvironment $environment
        $writer.Dispose()
    }

    It 'returns accepted input when command-marker output fails' {
        Enable-Prompt -Configuration (New-TestPromptConfiguration @{ ShellIntegration = 'Osc133' })
        try {
            [Console]::SetOut($writer)
            $line = PSConsoleHostReadLine
        }
        finally { [Console]::SetOut($originalOutput) }
        $line | Should -BeExactly 'accepted-command'
        [StatusLine.PromptSession]::LastTerminalError | Should -Match 'Terminal write failed'
    }

    It 'restores prompt and reader when terminal cleanup output fails' {
        $env:TERM_PROGRAM = 'contour'
        $reader = $function:global:PSConsoleHostReadLine
        Enable-Prompt -Configuration (New-TestPromptConfiguration @{ ShellIntegration = 'Osc133' })
        try {
            [Console]::SetOut($writer)
            Disable-Prompt -ErrorAction Stop
        }
        finally { [Console]::SetOut($originalOutput) }
        $function:global:prompt | Should -Be $originalPrompt
        $function:global:PSConsoleHostReadLine | Should -Be $reader
        [StatusLine.Prompt]::Configuration | Should -BeNullOrEmpty
        [StatusLine.PromptSession]::LastTerminalError | Should -Match 'Terminal write failed'
    }

    It 'detaches independent prompt ownership when module shutdown times out' {
        $blocked = [BlockingReviewWriter]::new()
        $startup = [IO.StringWriter]::new()
        $failure = $null
        $configuration = $null
        try {
            [Console]::SetOut($startup)
            Enable-Prompt -Configuration (New-TestPromptConfiguration)
            Enable-StatusLine -Block Clock -Force -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            Set-TestHostDeferral -Deferred:$false
            [Console]::SetOut($blocked)
            if (-not $blocked.Entered.Wait(5000)) { throw 'Worker did not reach blocked output.' }
            try { [StatusLine.ModuleCleanup]::new().OnRemove((Get-Module StatusLine)) }
            catch { $failure = $_ }
            $configuration = [StatusLine.Prompt]::Configuration
        }
        finally {
            $blocked.Release.Set()
            [Console]::SetOut($originalOutput)
            Disable-StatusLine -Force
            [StatusLine.ModuleCleanup]::new().OnImport()
            $blocked.Dispose()
            $startup.Dispose()
        }
        $failure.Exception.Message | Should -Match 'ownership is retained'
        $configuration | Should -BeNullOrEmpty
    }
}
