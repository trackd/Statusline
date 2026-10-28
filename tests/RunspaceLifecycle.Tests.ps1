BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Closed runspace cleanup' -Tag Lifecycle {
    It 'releases prompt, script caches and status ownership without Remove-Module' {
        $modulePath = (Get-Module StatusLine).Path
        $flags = [Reflection.BindingFlags]'NonPublic,Static'
        $sessions = [StatusLine.PromptSession].GetField('Sessions', $flags).GetValue($null)
        $runtime = [StatusLine.PromptSession].Assembly.GetType('StatusLine.Utility.ScriptBlockRuntime', $true)
        $owners = $runtime.GetField('Owners', $flags).GetValue($null)
        $baseline = $sessions.Count
        $originalPrompt = $function:global:prompt
        $originalReadLine = $function:global:PSConsoleHostReadLine
        $path = Join-Path $TestDrive 'closed-owner'
        foreach ($iteration in 1..3) {
            $other = [powershell]::Create()
            try {
                $null = $other.AddScript(@'
param($modulePath, $path)
Import-Module $modulePath
$definition = New-StatusLineBlock -ScriptBlock { 'owned' }
$null = [StatusLine.BlockSnapshots]::Snapshot([StatusLine.BlockSnapshotOptions]@{ Block = @($definition) })
Enable-Prompt -Left $definition -ShellIntegration None -ShellContext:$false
Enable-StatusLine -Block Clock -Target File -OutputPath $path -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
[runspace]::DefaultRunspace.InstanceId
'@).AddArgument($modulePath).AddArgument($path)
                $result = $other.Invoke()
                $other.Streams.Error.Count | Should -Be 0
                $owner = [guid]$result[0]
                $sessions.Count | Should -Be ($baseline + 1)
                $owners.ContainsKey($owner) | Should -BeTrue
            }
            finally { $other.Dispose() }
            $sessions.Count | Should -Be $baseline
            $owners.ContainsKey($owner) | Should -BeFalse
            $function:global:prompt | Should -Be $originalPrompt
            $function:global:PSConsoleHostReadLine | Should -Be $originalReadLine
            try {
                Enable-StatusLine -Block Clock -Target File -OutputPath $path -IntervalMilliseconds 100 -IdleTimeoutSeconds 0 -ErrorAction Stop
                [StatusLine.StatusBar]::Configuration | Should -Not -BeNullOrEmpty
            }
            finally { Disable-StatusLine }
        }
    }

    It 'shares command duration and resolved failure in either enable order, PromptFirst=<PromptFirst>' -ForEach @(
        @{ PromptFirst = $true }
        @{ PromptFirst = $false }
    ) {
        $other = [powershell]::Create()
        try {
            $null = $other.AddScript(@'
param($modulePath, $path, $promptFirst)
Import-Module $modulePath
$settings = [StatusLine.PromptConfiguration]@{
    Left = @('Duration', 'Status'); Right = @(); Input = @(); Block = @(); TransientBlock = @()
    ShellIntegration = 'None'; ShellContext = $false; Transient = $false
    Style = 'Plain'; IconSet = 'None'; Padding = ''; Symbol = '> '; Rows = 1
}
try {
    if ($promptFirst) { Enable-Prompt -Configuration $settings }
    Enable-StatusLine -Block Duration,Status -Target File -OutputPath $path -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
    if (-not $promptFirst) { Enable-Prompt -Configuration $settings }
    $now = Get-Date
    Add-History -InputObject ([pscustomobject]@{
        CommandLine = 'example'; ExecutionStatus = 'Completed'
        StartExecutionTime = $now.AddSeconds(-3); EndExecutionTime = $now
    })
    $global:LASTEXITCODE = 0
    Write-Error 'expected failure' -ErrorAction Ignore
    $text = prompt
    $updater = [StatusLine.PromptSession].Assembly.GetType('StatusLine.StatusLineUpdater')
    $snapshot = $updater.GetField('initialState', [Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
    [pscustomobject]@{ Prompt = $text; Duration = $snapshot.LastDuration; ExitCode = $snapshot.LastExitCode }
}
finally { Disable-StatusLine; Disable-Prompt; Remove-Module StatusLine }
'@).AddArgument((Get-Module StatusLine).Path).AddArgument((Join-Path $TestDrive 'snapshot')).AddArgument($PromptFirst)
            $result = $other.Invoke()
            $other.Streams.Error.Count | Should -Be 0
            $result[0].Prompt | Should -Match '3\.0s'
            $result[0].Duration | Should -Be ([timespan]::FromSeconds(3))
            $result[0].ExitCode | Should -Be 1
        }
        finally { $other.Dispose() }
    }
}
