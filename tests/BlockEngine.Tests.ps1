BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Block engine session contracts' -Tag Contract {
    It 'isolates script argument edits from other blocks in the same frame' {
        $definition = New-StatusLineBlock -ScriptBlock { param($state) $state.ErrorCount = 99; $state.ErrorCount }
        try {
            $options = [StatusLine.BlockSnapshotOptions]@{
                Block = @($definition, 'Errors'); IconSet = 'None'
                State = [StatusLine.StatusLineState]@{ ErrorCount = 3 }
            }
            $blocks = [StatusLine.BlockSnapshots]::Snapshot($options)
            $blocks[0].Text | Should -Be '99'
            $blocks[1].Text | Should -Be '3'
            $options.State.ErrorCount | Should -Be 3
        }
        finally { $definition.Dispose() }
    }

    It 'updates a selected definition directly without replacing it or recollecting content' {
        $definition = New-StatusLineBlock -Kind Date -Foreground '#123456'
        try {
            Enable-Prompt -Left $definition -Style Plain -ShellIntegration None -Transient:$false
            (& prompt) | Should -Match '38;2;18;52;86'
            $definition.Foreground = [StatusLine.TerminalColor]::new([Drawing.Color]::Blue)
            (& prompt) | Should -Match '38;2;0;0;255'
            $definition.Alignment = 'Left'
            { $definition.Alignment = [StatusLine.StatusLineAlignment]99 } | Should -Throw
            $definition.Alignment | Should -Be 'Left'
        }
        finally { Disable-Prompt; $definition.Dispose() }
    }

    It 'shows an ordinary script fault whose message contains timeout words' {
        $definition = New-StatusLineBlock -ScriptBlock { throw 'Upstream service timed out while reading data' }
        try {
            (New-TestBlock -Kind $definition).Text | Should -Match 'Upstream service timed out'
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($definition)).IsStale | Should -BeTrue
        }
        finally { $definition.Dispose() }
    }

    It 'previews errors from the calling session' {
        $record = [Management.Automation.ErrorRecord]::new(
            [InvalidOperationException]::new('preview-state'), 'PreviewState', 'NotSpecified', $null)
        $global:Error.Insert(0, $record)
        try {
            $configuration = New-TestPromptConfiguration @{
                Left = @('Errors'); Right = @(); Input = @(); Block = @()
                Style = 'Plain'; IconSet = 'None'; Padding = ''; Symbol = ''
            }
            (Show-Prompt -Configuration $configuration) | Should -Match 'ERR [1-9]'
        }
        finally { $global:Error.Remove($record) }
    }

    It 'infers duration for both direct and wrapped built-ins' {
        $historyEntry = [pscustomobject]@{
            CommandLine = 'block-engine-history'; ExecutionStatus = 'Completed'
            StartExecutionTime = [datetime]::Now.AddSeconds(-2); EndExecutionTime = [datetime]::Now
        }
        $historyEntry | Add-History
        $definition = New-StatusLineBlock -Kind Duration
        try {
            $direct = New-TestBlock -Kind Duration
            $wrapped = New-TestBlock -Kind $definition
            $wrapped.Text | Should -BeExactly $direct.Text
            $wrapped.Text | Should -Not -BeNullOrEmpty
        }
        finally {
            $definition.Dispose()
            foreach ($entry in Get-History | Where-Object CommandLine -EQ 'block-engine-history') {
                Clear-History -Id $entry.Id
            }
        }
    }

    It 'isolates a shared script by owner and survives the other owner unloading' {
        $modulePath = (Get-Module StatusLine).Path
        $definition = New-StatusLineBlock -ScriptBlock { param($state) $state.HistoryId } -IntervalMilliseconds 250
        $first = [powershell]::Create()
        $second = [powershell]::Create()
        $read = @'
param($modulePath, $definition, $historyId)
Import-Module -Name $modulePath
$options = [StatusLine.BlockSnapshotOptions]@{
    Block = @($definition); State = [StatusLine.StatusLineState]@{ HistoryId = $historyId }
    IconSet = 'None'; Padding = ''
}
$text = ([StatusLine.BlockSnapshots]::Snapshot($options)).Text
[pscustomobject]@{ Text = $text; Status = [StatusLine.RefreshDiagnostics]::GetScriptStatus($definition) }
'@
        try {
            $null = $first.AddScript($read).AddArgument($modulePath).AddArgument($definition).AddArgument(1)
            $firstResult = @($first.Invoke())[0]
            $firstResult.Text | Should -Be '1'
            $null = $second.AddScript($read).AddArgument($modulePath).AddArgument($definition).AddArgument(2)
            $secondResult = @($second.Invoke())[0]
            $secondResult.Text | Should -Be '2'
            $firstResult.Status.Owner | Should -Not -Be $secondResult.Status.Owner
            $firstResult.Status.CollectionId | Should -Not -Be $secondResult.Status.CollectionId
            $secondResult.Status.CollectionId | Should -BeGreaterThan 0
            $first.Commands.Clear()
            $null = $first.AddScript('Remove-Module StatusLine')
            $null = $first.Invoke()
            Start-Sleep -Milliseconds 300
            $second.Commands.Clear()
            $null = $second.AddScript($read).AddArgument($modulePath).AddArgument($definition).AddArgument(42)
            $refreshed = @($second.Invoke())[0]
            $refreshed.Text | Should -Be '42'
            $refreshed.Status.CollectionId | Should -Be $secondResult.Status.CollectionId
            $second.HadErrors | Should -BeFalse
        }
        finally { $first.Dispose(); $second.Dispose(); $definition.Dispose() }
    }
}
