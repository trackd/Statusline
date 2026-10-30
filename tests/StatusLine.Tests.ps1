BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Statusline output and lifecycle' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'emits the display type sequence for <Mode>' -ForEach @(
        @{ Mode = 'Hidden'; Code = 0 }
        @{ Mode = 'Indicator'; Code = 1 }
        @{ Mode = 'HostWritable'; Code = 2 }
    ) {
        Write-TestStatusLine -Mode $Mode -AsEscapeSequence | Should -BeExactly "$escape[0`$}$escape[$Code`$~"
    }

    It 'warns and does not start outside Contour' {
        $previous = $env:TERM_PROGRAM
        $env:TERM_PROGRAM = 'not-contour'
        try {
            Enable-StatusLine -Block Clock -Target HostWritable -WarningVariable startWarning -WarningAction SilentlyContinue
            $startWarning.Count | Should -Be 1
        }
        finally { $env:TERM_PROGRAM = $previous }
    }

    It 'stops cleanly when not running' {
        { Disable-StatusLine } | Should -Not -Throw
    }

    It 'rejects combined output targets before writing a file' {
        $path = Join-Path $TestDrive 'combined.txt'
        { Enable-StatusLine -Target 'HostWritable,File' -OutputPath $path -Force } | Should -Throw
        { Enable-StatusLine -Target ([StatusLine.StatusLineTarget]3) -OutputPath $path -Force } | Should -Throw
        Test-Path -LiteralPath $path | Should -BeFalse
    }

    It 'writes a single positioned line to a file for the File target' {
        $savedStyle = [StatusLine.DirectoryDisplay]::Style
        [StatusLine.DirectoryDisplay]::Style = 'Full'
        $previous = $env:TERM_PROGRAM
        $env:TERM_PROGRAM = 'not-contour'
        $path = Join-Path $TestDrive 'status.txt'
        try {
            Enable-StatusLine -Block Directory, Clock -Target File -OutputPath $path -IntervalMilliseconds 100 -Style Plain -IconSet None -WarningVariable startWarning -WarningAction SilentlyContinue
            $startWarning.Count | Should -Be 0
            $deadline = [DateTime]::UtcNow.AddSeconds(5)
            while (-not (Test-Path $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
            $text = Get-Content -LiteralPath $path -Raw
            $text | Should -Not -Match '\x1b\[\d+;\d+H|\x1b[78]|\$[~}]|[\r\n]'
            ($text -replace '\x1b\[[0-9;]*m') | Should -Match "^\s*$([regex]::Escape($PWD.Path))\s+\d{2}:\d{2}:\d{2}\s*$"
        }
        finally {
            Disable-StatusLine
            $env:TERM_PROGRAM = $previous
            [StatusLine.DirectoryDisplay]::Style = $savedStyle
        }
        Get-Content -LiteralPath $path -Raw | Should -BeNullOrEmpty
    }

    It 'selects host-writable mode once across redraws with deferred output <DeferHost>' -ForEach @(
        @{ DeferHost = $false }
        @{ DeferHost = $true }
    ) {
        $writer = [IO.StringWriter]::new()
        $original = [Console]::Out
        $timerTest = New-StatusLineBlock -Name timerTest -ScriptBlock { Get-Date -Format 'HH:mm:ss.fff' } -IntervalMilliseconds 250
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            Enable-StatusLine -Block $timerTest -Force -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            # Exercise both paths regardless of whether Pester was launched from a terminal or a pipe.
            Set-TestHostDeferral -Deferred:$DeferHost
            Wait-RefreshCondition {
                $null = $ExecutionContext.Events.GenerateEvent('PowerShell.OnIdle', $null, @(), $null, $true, $true)
                [regex]::Matches($writer.ToString(), '\x1b\[2K').Count -gt 1
            }
        }
        finally {
            Disable-StatusLine -Force
            [Console]::SetOut($original)
            $timerTest.Dispose()
        }
        [regex]::Matches($writer.ToString(), '\x1b\[2\$~').Count | Should -Be 1
        [regex]::Matches($writer.ToString(), '\x1b\[2K').Count | Should -BeGreaterThan 1
    }
}

Describe 'Status-Line Groups and Lifecycle' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'merges Kind with explicit left center and right groups without default kinds' {
        $path = Join-Path $TestDrive 'grouped.txt'
        Enable-StatusLine -Block Os -Left User -Center PowerShell -Right Host -Target File -OutputPath $path -Style Plain -IconSet None -IntervalMilliseconds 100
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(5)
            while (-not (Test-Path $path) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
            $text = (Get-Content $path -Raw) -replace '\x1b\[[0-9;]*m'
            $text | Should -Match ([regex]::Escape([Environment]::UserName))
            $text | Should -Match 'PS 7\.'
            # Long CI hostnames can be clipped by the right group's available width.
            $hostPrefix = [Environment]::MachineName.Substring(0, [Math]::Min(8, [Environment]::MachineName.Length))
            $text | Should -Match ([regex]::Escape($hostPrefix))
            $text | Should -Not -Match 'PROC|RAM|CPU'
        }
        finally { Disable-StatusLine }
    }

    It 'supports enabling and disabling repeatedly without a separate suspended state' {
        $path = Join-Path $TestDrive 'reenabled.txt'
        try {
            $lifecycleFirst = New-StatusLineBlock -Name lifecycleFirst -ScriptBlock { 'FIRST' }
            $lifecycleSecond = New-StatusLineBlock -Name lifecycleSecond -ScriptBlock { 'SECOND' }
            Enable-StatusLine -Block $lifecycleFirst -Target File -OutputPath $path -IntervalMilliseconds 100
            Enable-StatusLine -Block $lifecycleSecond -Target File -OutputPath $path -IntervalMilliseconds 100
            $deadline = [DateTime]::UtcNow.AddSeconds(5)
            while ((-not (Test-Path $path) -or [string](Get-Content $path -Raw) -notmatch 'SECOND') -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
            (Get-Content $path -Raw) | Should -Match 'SECOND'
            Disable-StatusLine
            Disable-StatusLine
        }
        finally { Disable-StatusLine; $lifecycleFirst.Dispose(); $lifecycleSecond.Dispose() }
    }
}

Describe 'Shared state and selected providers' -Tag Lifecycle {
    It 'does not execute an unselected script definition while selected blocks refresh' {
        $activePath = Join-Path $TestDrive 'active-provider.txt'
        $unusedPath = Join-Path $TestDrive 'unused-provider.txt'
        $activeScript = [scriptblock]::Create("Add-Content -LiteralPath '$($activePath.Replace("'", "''"))' -Value tick; 'ACTIVE'")
        $unusedScript = [scriptblock]::Create("Set-Content -LiteralPath '$($unusedPath.Replace("'", "''"))' -Value unused; 'UNUSED'")
        $activeProvider = New-StatusLineBlock -Name activeProvider -ScriptBlock $activeScript -IntervalMilliseconds 250
        $unusedProvider = New-StatusLineBlock -Name unusedProvider -ScriptBlock $unusedScript -IntervalMilliseconds 250
        try {
            Enable-StatusLine -Block $activeProvider -Target File -OutputPath (Join-Path $TestDrive 'selected.txt') -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            Wait-RefreshCondition { (Test-Path $activePath) -and @(Get-Content $activePath).Count -ge 3 }
            Test-Path $unusedPath | Should -BeFalse
        }
        finally {
            Disable-StatusLine
            $activeProvider.Dispose()
            $unusedProvider.Dispose()
        }
    }

    It 'shares prompt session counters with the statusline without an opt-in property' {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Block = @(); Input = @()
            Rows = 1; Symbol = '> '; Transient = $false; ShellIntegration = 'None'
        }
        $path = Join-Path $TestDrive 'shared-state.txt'
        $retainedError = [Management.Automation.ErrorRecord]::new([Exception]::new('shared snapshot'), 'SharedSnapshot', 'NotSpecified', $null)
        try {
            Enable-Prompt -Configuration $configuration
            Enable-StatusLine -Block Errors -Target File -OutputPath $path -Style Plain -IconSet None -Padding '=' -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            $global:Error.Insert(0, $retainedError)
            $expected = "=ERR $($global:Error.Count)="
            $null = & prompt
            Wait-RefreshCondition { (Test-Path $path) -and (((Read-TestStatusFile -Path $path) -replace '\x1b\[[0-9;]*m', '').Contains($expected)) }
        }
        finally {
            $global:Error.Remove($retainedError)
            Disable-StatusLine
            Disable-Prompt
        }
    }
}
