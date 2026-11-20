BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Context and responsive blocks' -Tag Contract {
    It 'leaves command hints to the shell instead of exposing module hint parameters' {
        foreach ($commandName in 'Enable-Prompt', 'Enable-StatusLine') {
            (Get-Command $commandName).Parameters.Keys | Should -Not -Contain 'EnableHints'
        }
        (Get-Command New-StatusLineBlock).Parameters.Keys | Should -Not -Contain 'Commands'
        [StatusLine.PromptConfiguration].GetProperty('EnableHints') | Should -BeNullOrEmpty
        [StatusLine.StatusBarConfiguration].GetProperty('EnableHints') | Should -BeNullOrEmpty
        [StatusLine.StatusLineBlockDefinition].GetProperty('Commands') | Should -BeNullOrEmpty
    }
    It 'uses preview width for visibility and retains the higher priority block' {
        $important = New-StatusLineBlock -Kind UserHost -Icon '' -Priority 10
        $decorative = New-StatusLineBlock -Kind Host -Icon '' -Priority -10 -MinimumWidth 20
        $configuration = [StatusLine.StatusBarConfiguration]@{
            Left = @($decorative, $important)
            Style = 'Plain'
            Padding = ''
        }
        $identity = (New-TestBlock -Kind UserHost -IconSet None).Text
        $text = (Show-StatusLine $configuration -Width $identity.Length) -replace '\x1b\[[0-9;]*m'
        $text.Trim() | Should -BeExactly $identity
        $important.MinimumWidth = 100
        (Show-StatusLine $configuration -Width 19) -replace '\x1b\[[0-9;]*m' | Should -BeNullOrEmpty
    }

    It 'counts active jobs from the owning foreground runspace' {
        $job = Start-Job { Start-Sleep -Seconds 30 }
        try {
            (New-TestBlock -Kind Jobs).Text | Should -Match '^[1-9][0-9]*$'
        }
        finally { Stop-Job $job; Remove-Job $job }
    }


    It 'refreshes the ViMode block without input observation' {
        $runspace = [runspace]::DefaultRunspace
        try {
            Enable-Prompt -Left ViMode -ShellIntegration None
            $null = & prompt
            [StatusLine.StatusLineActivity]::BeginReadLine($runspace.InstanceId)
            [StatusLine.PromptSession]::SetReading($runspace, $true)
            foreach ($mode in 'NORMAL', 'INSERT') {
                [StatusLine.StatusLineActivity]::ObserveEditorMode($runspace.InstanceId, $mode, $ExecutionContext.SessionState)
                Wait-RefreshCondition {
                    if ([StatusLine.PromptSession]::BeginResize($runspace)) {
                        try { (& prompt).Contains($mode) }
                        finally { [StatusLine.PromptSession]::EndResize($runspace) }
                    }
                    else { $false }
                }
            }
        }
        finally {
            [StatusLine.PromptSession]::EndResize($runspace)
            [StatusLine.PromptSession]::SetReading($runspace, $false)
            [StatusLine.StatusLineActivity]::EndReadLine($runspace.InstanceId)
            Disable-Prompt
        }
    }

    It 'does not refresh editor mode owned by <State> from a queued idle refresh' -ForEach @(
        @{ State = 'line acceptance'; AcceptedInput = $true; SelectionCount = 0 }
        @{ State = 'completion menu'; AcceptedInput = $false; SelectionCount = 1 }
    ) {
        param($AcceptedInput, $SelectionCount)
        Import-Module PSReadLine
        $editorType = [Microsoft.PowerShell.PSConsoleReadLine]
        $instanceFlags = [Reflection.BindingFlags]'Instance,NonPublic'
        $staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
        $editor = $editorType.GetField('_singleton', $staticFlags).GetValue($null)
        $ready = $editorType.GetField('_readLineReady', $instanceFlags)
        $accepted = $editorType.GetField('_inputAccepted', $instanceFlags)
        $selection = $editorType.GetField('_visualSelectionCommandCount', $instanceFlags)
        $oldReady = $ready.GetValue($editor)
        $oldAccepted = $accepted.GetValue($editor)
        $oldSelection = $selection.GetValue($editor)
        $script = [StatusLine.PromptSession].GetField('InstallScript', $staticFlags).GetRawConstantValue()
        $ast = [Management.Automation.Language.Parser]::ParseInput($script, [ref]$null, [ref]$null)
        $assignment = $ast.Find({
            param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
                $node.Left.VariablePath.UserPath -eq 'resizeAction'
        }, $true)
        $actionAst = $assignment.Right.Find({ param($node) $node -is [Management.Automation.Language.ScriptBlockExpressionAst] }, $true)
        $action = $actionAst.ScriptBlock.GetScriptBlock()
        $runspace = [runspace]::DefaultRunspace
        try {
            Enable-Prompt -Left ViMode -ShellIntegration None
            $null = & prompt
            [StatusLine.StatusLineActivity]::BeginReadLine($runspace.InstanceId)
            [StatusLine.PromptSession]::SetReading($runspace, $true)
            [StatusLine.StatusLineActivity]::ObserveEditorMode($runspace.InstanceId, 'NORMAL', $ExecutionContext.SessionState)
            # AcceptLine clears predictions before the enclosing ReadLine call returns.
            $ready.SetValue($editor, $true)
            $accepted.SetValue($editor, $AcceptedInput)
            $selection.SetValue($editor, $SelectionCount)
            { & $action } | Should -Not -Throw
            $contextType = [StatusLine.PromptSession].Assembly.GetType('StatusLine.EditorContext')
            $context = $contextType.GetMethod('Get', $staticFlags).Invoke($null, @($runspace.InstanceId))
            $context.ViMode | Should -BeExactly 'NORMAL'
        }
        finally {
            $ready.SetValue($editor, $oldReady)
            $accepted.SetValue($editor, $oldAccepted)
            $selection.SetValue($editor, $oldSelection)
            [StatusLine.PromptSession]::SetReading($runspace, $false)
            [StatusLine.StatusLineActivity]::EndReadLine($runspace.InstanceId)
            Disable-Prompt
        }
    }

    It 'shows SSH-only blocks only with an SSH session marker' {
        $names = 'SSH_CONNECTION', 'SSH_CLIENT', 'SSH_TTY'
        $saved = @{}
        foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $null) }
        try {
            $remote = New-StatusLineBlock -Kind Host -SshOnly -Icon ''
            $configuration = [StatusLine.StatusBarConfiguration]@{ Left = @($remote); Style = 'Plain'; Padding = '' }
            (Show-StatusLine $configuration) -replace '\x1b\[[0-9;]*m' | Should -BeNullOrEmpty
            $env:SSH_CONNECTION = 'test'
            Show-StatusLine $configuration | Should -Match ([regex]::Escape([Environment]::MachineName))
        }
        finally { foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
    }
}

Describe 'Temporary status messages' -Tag Contract {
    AfterEach { Disable-StatusLine }

    It 'restores configured blocks when the message expires and clears explicitly' {
        $path = Join-Path $TestDrive 'messages.txt'
        Enable-StatusLine -Block Host -IconSet None -Style Plain -Target File -OutputPath $path -IntervalMilliseconds 100
        Wait-RefreshCondition { (Test-Path $path) -and (Get-Content $path -Raw) -match [regex]::Escape([Environment]::MachineName) }
        [StatusLine.StatusBar]::ShowMessage('Temporary context', 1000)
        Wait-RefreshCondition { (Get-Content $path -Raw) -match 'Temporary context' }
        Wait-RefreshCondition { (Get-Content $path -Raw) -notmatch 'Temporary context' -and (Get-Content $path -Raw) -match [regex]::Escape([Environment]::MachineName) }
        [StatusLine.StatusBar]::ShowMessage('Clear this', 60000)
        Wait-RefreshCondition { (Get-Content $path -Raw) -match 'Clear this' }
        [StatusLine.StatusBar]::ClearMessage()
        Wait-RefreshCondition { (Get-Content $path -Raw) -notmatch 'Clear this' }
        { [StatusLine.StatusBar]::ShowMessage('invalid', 0) } | Should -Throw
    }

    It 'shows completion messages only at the configured duration threshold' {
        $path = Join-Path $TestDrive 'completion.txt'
        Enable-StatusLine -Block Host -Target File -OutputPath $path -IntervalMilliseconds 100 -CompletionMessageThresholdMilliseconds 2000
        $updater = [StatusLine.StatusBar].Assembly.GetType('StatusLine.StatusLineUpdater')
        $complete = $updater.GetMethod('CommandCompleted', [Reflection.BindingFlags]'Static,NonPublic')
        $ownerId = [runspace]::DefaultRunspace.InstanceId
        $null = $complete.Invoke($null, @($ownerId, 1, [timespan]::FromSeconds(2)))
        Wait-RefreshCondition { (Test-Path $path) -and (Get-Content $path -Raw) -match 'Failed \(1\) in 2.0s' }
        [StatusLine.StatusBar]::ClearMessage()
        $null = $complete.Invoke($null, @($ownerId, 0, [timespan]::FromMilliseconds(1999)))
        Wait-RefreshCondition { (Get-Content $path -Raw) -match [regex]::Escape([Environment]::MachineName) }
        (Get-Content $path -Raw) | Should -Not -Match 'Completed'
    }
}
