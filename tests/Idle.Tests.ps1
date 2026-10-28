BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $updaterType = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.StatusLineUpdater')
    $activityField = $updaterType.GetField('activity', [Reflection.BindingFlags]'Static,NonPublic')
}

Describe 'Idle refresh lifecycle' -Tag Lifecycle {
    AfterEach { Disable-StatusLine }

    It 'stops refreshing while idle and resumes on shell activity' {
        $path = Join-Path $TestDrive 'idle-status'
        $idleClock = New-StatusLineBlock -Name idleClock -ScriptBlock { Get-Date -Format 'HH:mm:ss.fffffff' } -IntervalMilliseconds 250
        try {
            Enable-StatusLine -Block $idleClock -Target File -OutputPath $path -IntervalMilliseconds 100 -IdleTimeoutSeconds 1
            $activity = $activityField.GetValue($null)
            Wait-RefreshCondition { $activity.IsIdle }
            $before = Read-TestStatusFile -Path $path
            Start-Sleep -Milliseconds 350
            Read-TestStatusFile -Path $path | Should -BeExactly $before
            [StatusLine.StatusLineActivity]::CommandAccepted([runspace]::DefaultRunspace.InstanceId, '')
            Wait-RefreshCondition { (Read-TestStatusFile -Path $path) -ne $before }
            $activity.IsIdle | Should -BeFalse
        }
        finally { Disable-StatusLine; $idleClock.Dispose() }
    }

    It 'handles OnIdle and removes only its own subscriptions' {
        $before = @(Get-EventSubscriber -Force | Where-Object SourceIdentifier -in 'PowerShell.OnIdle', 'PowerShell.Exiting')
        Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'idle-event') -IdleTimeoutSeconds 1
        $activity = $activityField.GetValue($null)
        $activity.GetType().GetField('lastActivity', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($activity, [long]0)
        $null = $ExecutionContext.Events.GenerateEvent('PowerShell.OnIdle', $null, @(), $null, $true, $true)
        $activity.IsIdle | Should -BeTrue
        Disable-StatusLine
        $after = @(Get-EventSubscriber -Force | Where-Object SourceIdentifier -in 'PowerShell.OnIdle', 'PowerShell.Exiting')
        $after.Count | Should -Be $before.Count
        foreach ($subscription in $before) { $after.SubscriptionId | Should -Contain $subscription.SubscriptionId }
    }

    It 'allows idle pausing to be disabled' {
        Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'no-idle') -IdleTimeoutSeconds 0
        $activity = $activityField.GetValue($null)
        $activity.GetType().GetField('lastActivity', [Reflection.BindingFlags]'Instance,NonPublic').SetValue($activity, [long]0)
        $null = $ExecutionContext.Events.GenerateEvent('PowerShell.OnIdle', $null, @(), $null, $true, $true)
        $activity.IsIdle | Should -BeFalse
    }

    It 'pauses during an unfinished input line without any OnIdle events' {
        $path = Join-Path $TestDrive 'unfinished-input'
        $unfinishedClock = New-StatusLineBlock -Name unfinishedClock -ScriptBlock { Get-Date -Format 'HH:mm:ss.fffffff' } -IntervalMilliseconds 250
        try {
            Enable-StatusLine -Block $unfinishedClock -Target File -OutputPath $path -IntervalMilliseconds 100 -IdleTimeoutSeconds 1
            # A reader must clear the previous command state before waiting for further input.
            [StatusLine.StatusLineActivity]::CommandAccepted([runspace]::DefaultRunspace.InstanceId, 'previous command')
            [StatusLine.StatusLineActivity]::BeginReadLine([runspace]::DefaultRunspace.InstanceId)
            Wait-RefreshCondition { [StatusLine.RefreshDiagnostics]::IsIdle }
            $before = [IO.File]::ReadAllText($path)
            Start-Sleep -Milliseconds 350
            [IO.File]::ReadAllText($path) | Should -BeExactly $before
            [StatusLine.StatusLineActivity]::CommandAccepted([runspace]::DefaultRunspace.InstanceId, '')
            Wait-RefreshCondition { -not [StatusLine.RefreshDiagnostics]::IsIdle }
        }
        finally { Disable-StatusLine; $unfinishedClock.Dispose() }
    }
}
