BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    if (-not ('BlockingReviewWriter' -as [type])) {
        Add-Type -Path "$PSScriptRoot/BlockingReviewWriter.cs"
    }
}

Describe 'Terminal command boundaries and ownership' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'retains worker ownership when terminal output blocks past the stop deadline' {
        $writer = [BlockingReviewWriter]::new()
        $startupWriter = [IO.StringWriter]::new()
        $original = [Console]::Out
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($startupWriter))
            Enable-StatusLine -Block Clock -Force -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            Set-TestHostDeferral -Deferred:$false
            [Console]::SetOut($writer)
            $writer.Entered.Wait(5000) | Should -BeTrue
            { Disable-StatusLine -Force -ErrorAction Stop } | Should -Throw '*ownership is retained*'
            $updater = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.StatusLineUpdater')
            $updater.GetProperty('IsRunning').GetValue($null) | Should -BeTrue
        }
        finally {
            $writer.Release.Set()
            Disable-StatusLine -Force
            [Console]::SetOut($original)
            $writer.Dispose()
            $startupWriter.Dispose()
        }
    }

    It 'restores prompt and reader functions in either enable/disable order' -ForEach @(
        @{ PromptFirst = $true; StopFirst = $true }
        @{ PromptFirst = $false; StopFirst = $false }
        @{ PromptFirst = $true; StopFirst = $false }
        @{ PromptFirst = $false; StopFirst = $true }
    ) {
        $oldPrompt = $function:global:prompt
        $oldReadLine = $function:global:PSConsoleHostReadLine
        try {
            if ($PromptFirst) { Enable-Prompt -Configuration (New-TestPromptConfiguration) -Transient:$false -Left Directory -Right Clock -ShellIntegration None }
            Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'ordered-hooks')
            if (-not $PromptFirst) { Enable-Prompt -Configuration (New-TestPromptConfiguration) -Transient:$false -Left Directory -Right Clock -ShellIntegration None }
            if ($StopFirst) { Disable-StatusLine; Disable-Prompt }
            else { Disable-Prompt; Disable-StatusLine }
            $function:global:prompt | Should -Be $oldPrompt
            $function:global:PSConsoleHostReadLine | Should -Be $oldReadLine
        }
        finally { Disable-StatusLine; Disable-Prompt; $function:global:prompt = $oldPrompt; $function:global:PSConsoleHostReadLine = $oldReadLine }
    }

    It 'handles command activity for the <Target> target' -ForEach @(
        @{ Target = 'HostWritable' }
        @{ Target = 'File' }
    ) {
        $writer = [IO.StringWriter]::new()
        $original = [Console]::Out
        $ownerId = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId
        $path = Join-Path $TestDrive 'busy-status'
        $busyClock = New-StatusLineBlock -Name busyClock -ScriptBlock { Get-Date -Format 'HH:mm:ss.fffffff' } -IntervalMilliseconds 250
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            Enable-StatusLine -Block $busyClock -Target $Target -OutputPath $path -Force -IntervalMilliseconds 100
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('[2K') -or (Test-Path -LiteralPath $path) }
            [StatusLine.StatusLineActivity]::CommandAccepted($ownerId, 'native application')
            $before = $writer.ToString()
            $fileBefore = if ($Target -eq 'File') { Get-Content -LiteralPath $path -Raw }
            Start-Sleep -Milliseconds 650
            $writer.ToString() | Should -BeExactly $before
            if ($Target -eq 'File') { (Get-Content -LiteralPath $path -Raw) | Should -Not -Be $fileBefore }
            [StatusLine.StatusLineActivity]::PromptReady($ownerId)
            if ($Target -eq 'HostWritable') { Wait-RefreshCondition -FlushHost { $writer.ToString().Length -gt $before.Length } }
        }
        finally { Disable-StatusLine -Force; [Console]::SetOut($original); $busyClock.Dispose() }
    }

    It 'preserves the success value observed by the wrapped prompt' {
        $oldPrompt = $function:global:prompt
        $function:global:prompt = { $previousSuccess = $?; "success=$previousSuccess" }
        try {
            Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'success-boundary')
            Write-Error -Message 'expected failure' -ErrorAction Ignore
            $result = prompt
            $result | Should -BeExactly 'success=False'
        }
        finally { Disable-StatusLine -Force; $function:global:prompt = $oldPrompt }
    }

    It 'keeps status frames out of twenty blank-input redraws even with UpdateDuringCommand' {
        $writer = [IO.StringWriter]::new()
        $original = [Console]::Out
        $ownerId = [runspace]::DefaultRunspace.InstanceId
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            $blankRefresh = New-StatusLineBlock -Name blankRefresh -ScriptBlock { Get-Date -Format 'HH:mm:ss.fffffff' } -IntervalMilliseconds 250
            $options = @{
                Block = $blankRefresh
                Force = $true
                IdleTimeoutSeconds = 0
                UpdateDuringCommand = $true
                IntervalMilliseconds = 100
            }
            Enable-StatusLine @options
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('[2K') }
            foreach ($iteration in 1..20) {
                [StatusLine.StatusLineActivity]::EndReadLine($ownerId)
                $before = $writer.ToString()
                [StatusLine.StatusLineActivity]::CommandAccepted($ownerId, '')
                [StatusLine.StatusLineActivity]::PromptReady($ownerId)
                Start-Sleep -Milliseconds 30
                $writer.ToString() | Should -BeExactly $before
                [StatusLine.StatusLineActivity]::BeginReadLine($ownerId)
            }
            $before = $writer.ToString()
            Wait-RefreshCondition -FlushHost { $writer.ToString().Length -gt $before.Length }
        }
        finally {
            Disable-StatusLine -Force
            [Console]::SetOut($original)
            $blankRefresh.Dispose()
        }
    }

    It 'commits queued interactive status frames on the foreground idle action' {
        $writer = [IO.StringWriter]::new()
        $original = [Console]::Out
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            Enable-StatusLine -Block Clock -Force -IntervalMilliseconds 100
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('[2K') }
            [StatusLine.StatusLineActivity]::EndReadLine([runspace]::DefaultRunspace.InstanceId)
            Set-TestHostDeferral -Deferred
            $before = $writer.ToString()
            Start-Sleep -Milliseconds 1200
            [StatusLine.StatusLineActivity]::BeginReadLine([runspace]::DefaultRunspace.InstanceId)
            $writer.ToString() | Should -BeExactly $before
            Wait-RefreshCondition {
                $null = $ExecutionContext.Events.GenerateEvent('PowerShell.OnIdle', $null, @(), $null, $true, $true)
                $writer.ToString().Length -gt $before.Length
            }
            $writer.ToString() | Should -Not -Match '\x1b\[1;1H'
        }
        finally {
            Disable-StatusLine -Force
            [Console]::SetOut($original)
        }
    }

    It 'keeps registrations and updater ownership isolated between runspaces' {
        $modulePath = (Get-Module StatusLine).Path
        $other = [powershell]::Create()
        $ownerBlock = New-StatusLineBlock -Name ownerBlock -ScriptBlock { 'owner' }
        $path = Join-Path $TestDrive 'owned-status'
        try {
            Enable-StatusLine -Block Clock -Target File -OutputPath $path -IntervalMilliseconds 100
            Wait-RefreshCondition { (Test-Path -LiteralPath $path) -and (Read-TestStatusFile -Path $path).Length -gt 0 }
            $scriptText = @'
param($modulePath, $outputPath)
Import-Module -Name $modulePath
$ownerBlock = New-StatusLineBlock -Name ownerBlock -ScriptBlock { 'other' }
$text = ([StatusLine.BlockSnapshots]::Snapshot([StatusLine.BlockSnapshotOptions]@{ Block = @($ownerBlock) })).Text
$startRejected = $false
try { Enable-StatusLine -Block Clock -Target File -OutputPath $outputPath -ErrorAction Stop }
catch { $startRejected = $true }
Remove-Module StatusLine
[pscustomobject]@{Text = $text; StartRejected = $startRejected}
'@
            $null = $other.AddScript($scriptText).AddArgument($modulePath).AddArgument($path)
            $result = $other.Invoke()
            $result[0].Text | Should -BeExactly 'other'
            $result[0].StartRejected | Should -BeTrue
            (New-TestBlock -Kind $ownerBlock).Text | Should -BeExactly 'owner'
            (Read-TestStatusFile -Path $path).Length | Should -BeGreaterThan 0
        }
        finally { $other.Dispose(); Disable-StatusLine; $ownerBlock.Dispose() }
    }
}
