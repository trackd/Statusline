BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $updaterType = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.StatusLineUpdater')
    $workerField = $updaterType.GetField('thread', [Reflection.BindingFlags]'Static,NonPublic')
}

Describe 'Live prompt configuration' -Tag Contract {
    AfterEach { Disable-Prompt }

    It 'returns no configuration when disabled and rejects updates' {
        Disable-Prompt
        [StatusLine.Prompt]::Configuration | Should -BeNullOrEmpty
        { [StatusLine.Prompt]::SetColors('#000000', '#FFFFFF') } | Should -Throw
    }

    It 'changes blocks, colors and presentation without reinstalling either hook' {
        $first = [StatusLine.BlockSnapshots]::Create('FIRST')
        $next = [StatusLine.BlockSnapshots]::Create('NEXT')
        $configuration = New-TestPromptConfiguration @{
            Left = @($first); Right = @(); Input = @(); Block = @()
            Rows = 1; Style = 'Plain'; Padding = ''; Symbol = '> '
            ShellIntegration = 'None'; Transient = $false
        }
        Enable-Prompt -Configuration $configuration
        $promptHook = $function:global:prompt
        $readerHook = $function:global:PSConsoleHostReadLine
        [StatusLine.Prompt]::SetBlocks('Left', @($next))
        [StatusLine.Prompt]::SetColors('#123456', '#ABCDEF')
        [StatusLine.Prompt]::Configure(@{ Symbol = '$ '; ConnectRows = $false })
        $text = & prompt
        ($text -replace '[[0-9;]*m') | Should -BeExactly 'NEXT$ '
        $text | Should -Match '38;2;18;52;86'
        $function:global:prompt | Should -Be $promptHook
        $function:global:PSConsoleHostReadLine | Should -Be $readerHook
        [StatusLine.Prompt]::AddBlock('Left', $first)
        [StatusLine.Prompt]::Configuration.Left.Count | Should -Be 2
        [StatusLine.Prompt]::RemoveBlock('Left', $next)
        [StatusLine.Prompt]::Configuration.Left.Count | Should -Be 1
        ((& prompt) -replace '[[0-9;]*m') | Should -BeExactly 'FIRST$ '
    }

    It 'returns detached snapshots and validates whole changes before applying' {
        $first = [StatusLine.BlockSnapshots]::Create('FIRST')
        $configuration = New-TestPromptConfiguration @{
            Left = @($first); Right = @(); Input = @(); Block = @()
            Rows = 1; Style = 'Plain'; Padding = ''; Symbol = '> '
            ShellIntegration = 'None'; Transient = $false; StartColor = $null; EndColor = $null
        }
        Enable-Prompt -Configuration $configuration
        $snapshot = [StatusLine.Prompt]::Configuration
        $snapshot.Left[0] = [StatusLine.BlockSnapshots]::Create('MUTATED')
        ((& prompt) -replace '[[0-9;]*m') | Should -BeExactly 'FIRST> '
        { [StatusLine.Prompt]::Configure(@{ Symbol = 'bad'; StartColor = '#123456' }) } | Should -Throw
        { [StatusLine.Prompt]::Configure(@{ ShellIntegration = 'Osc133' }) } | Should -Throw
        { [StatusLine.Prompt]::Configure(@{ Blok = @() }) } | Should -Throw
        [StatusLine.Prompt]::Configuration.Symbol | Should -Be '> '
        [StatusLine.Prompt]::Configuration.StartColor | Should -BeNullOrEmpty
        $selection = @([StatusLine.BlockSnapshots]::Create('DETACHED'))
        [StatusLine.Prompt]::SetBlocks('Left', $selection)
        $selection[0] = $first
        ((& prompt) -replace '[[0-9;]*m') | Should -BeExactly 'DETACHED> '
    }

    It 'retains the old transient output until the next normal prompt draw' {
        $old = [StatusLine.BlockSnapshots]::Create('OLD')
        $next = [StatusLine.BlockSnapshots]::Create('NEW')
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @(); Block = @()
            Rows = 1; Style = 'Plain'; Padding = ''; TransientSymbol = '> '
            ShellIntegration = 'None'; Transient = $true; TransientBlock = @($old)
        }
        Enable-Prompt -Configuration $configuration
        $null = & prompt
        [StatusLine.Prompt]::SetBlocks('Transient', @($next))
        [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
        ((& prompt) -replace '[[0-9;]*m') | Should -BeExactly 'OLD> '
        [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
        $null = & prompt
        [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
        ((& prompt) -replace '[[0-9;]*m') | Should -BeExactly 'NEW> '
        [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
    }

    It 'toggles transient handling without replacing the reader' {
        Enable-Prompt -Configuration (New-TestPromptConfiguration @{
            Transient = $false; TransientBlock = @(); ShellIntegration = 'None'
            Left = @(); Right = @(); Input = @()
        })
        $reader = $function:global:PSConsoleHostReadLine
        [StatusLine.Prompt]::SetBlocks('Transient', @('Duration'))
        [StatusLine.PromptSession]::IsTransientEnabled([runspace]::DefaultRunspace) | Should -BeTrue
        [StatusLine.Prompt]::Configure(@{ Transient = $false })
        [StatusLine.PromptSession]::IsTransientEnabled([runspace]::DefaultRunspace) | Should -BeFalse
        $function:global:PSConsoleHostReadLine | Should -Be $reader
    }
}

Describe 'Live statusbar configuration' -Tag Contract {
    AfterEach { Disable-StatusLine; Disable-Prompt }

    It 'returns no configuration when disabled and rejects updates' {
        Disable-StatusLine
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
        { [StatusLine.StatusBar]::SetColors('#000000', '#FFFFFF') } | Should -Throw
    }

    It 'updates file output and settings while retaining the worker and hooks' {
        $first = New-StatusLineBlock -ScriptBlock { 'FIRST' } -Alignment Left
        $next = New-StatusLineBlock -ScriptBlock { 'NEXT' } -Alignment Left
        $path = Join-Path $TestDrive 'live-statusline'
        $options = @{
            Block = @($first); Target = 'File'; OutputPath = $path
            IntervalMilliseconds = 100; IdleTimeoutSeconds = 0
            Style = 'Plain'; IconSet = 'None'; Padding = ''
            StartColor = '#000000'; EndColor = '#FFFFFF'; UpdateDuringCommand = $false
        }
        try {
            Enable-StatusLine @options
            Wait-RefreshCondition { (Test-Path $path) -and (Get-Content $path -Raw).Contains('FIRST') }
            $worker = $workerField.GetValue($null)
            $promptHook = $function:global:prompt
            $readerHook = $function:global:PSConsoleHostReadLine
            [StatusLine.StatusBar]::Configuration.Block[0] | Should -Be $first
            [StatusLine.StatusBar]::SetBlocks('Block', @($next))
            [StatusLine.StatusBar]::SetColors('#123456', '#ABCDEF')
            [StatusLine.StatusBar]::Configure(@{
                IntervalMilliseconds = 150; UpdateDuringCommand = $true
                WeatherLocation = 'Malmö'; BlockTimeoutMilliseconds = 1200; Padding = '.'
            })
            Wait-RefreshCondition {
                $text = Get-Content $path -Raw
                $text.Contains('NEXT') -and !$text.Contains('FIRST') -and $text.Contains('38;2;18;52;86')
            }
            $configuration = [StatusLine.StatusBar]::Configuration
            $configuration.IntervalMilliseconds | Should -Be 150
            $configuration.UpdateDuringCommand | Should -BeTrue
            $configuration.WeatherLocation | Should -Be 'Malmö'
            $configuration.BlockTimeoutMilliseconds | Should -Be 1200
            $configuration.Padding | Should -Be '.'
            $configuration.OutputPath | Should -Be $path
            $workerField.GetValue($null) | Should -Be $worker
            $function:global:prompt | Should -Be $promptHook
            $function:global:PSConsoleHostReadLine | Should -Be $readerHook
            $configuration.Block[0] = $first
            [StatusLine.StatusBar]::Configuration.Block[0] | Should -Be $next
            [StatusLine.StatusBar]::AddBlock('Left', 'Clock')
            [StatusLine.StatusBar]::Configuration.Left.Count | Should -Be 1
            [StatusLine.StatusBar]::RemoveBlock('Left', 'clock')
            [StatusLine.StatusBar]::Configuration.Left.Count | Should -Be 0
            { [StatusLine.StatusBar]::Configure(@{ OutputPath = "$path-other" }) } | Should -Throw
            { [StatusLine.StatusBar]::Configure(@{ Target = 'HostWritable' }) } | Should -Throw
            { [StatusLine.StatusBar]::Configure(@{ Padding = 'bad'; IntervalMilliseconds = 1 }) } | Should -Throw
            [StatusLine.StatusBar]::Configuration.Padding | Should -Be '.'
        }
        finally { Disable-StatusLine; $first.Dispose(); $next.Dispose() }
    }

    It 'starts newly selected providers and stops requesting removed ones' {
        $marker = Join-Path $TestDrive 'live-provider-ticks'
        $script = [scriptblock]::Create("Add-Content -LiteralPath '$marker' tick; 'ACTIVE'")
        $definition = New-StatusLineBlock -ScriptBlock $script -IntervalMilliseconds 250 -Alignment Left
        try {
            Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'demand-driven') -IntervalMilliseconds 100 -IdleTimeoutSeconds 0
            Test-Path $marker | Should -BeFalse
            [StatusLine.StatusBar]::AddBlock('Left', $definition)
            Wait-RefreshCondition { (Test-Path $marker) -and @(Get-Content $marker).Count -ge 2 }
            [StatusLine.StatusBar]::RemoveBlock('Left', $definition)
            Start-Sleep -Milliseconds 500
            $count = @(Get-Content $marker).Count
            Start-Sleep -Milliseconds 600
            @(Get-Content $marker).Count | Should -Be $count
        }
        finally { Disable-StatusLine; $definition.Dispose() }
    }

    It 'keeps runtime modification ownership scoped to the caller' {
        $modulePath = (Get-Module StatusLine).Path
        Enable-Prompt -Configuration (New-TestPromptConfiguration @{ Left = @(); Right = @(); ShellIntegration = 'None'; Transient = $false })
        Enable-StatusLine -Block Clock -Target File -OutputPath (Join-Path $TestDrive 'owned-runtime') -IntervalMilliseconds 100
        $other = [powershell]::Create()
        try {
            $null = $other.AddScript({
                param($modulePath)
                Import-Module $modulePath
                $promptAbsent = $null -eq [StatusLine.Prompt]::Configuration
                $barAbsent = $null -eq [StatusLine.StatusBar]::Configuration
                $rejected = $false
                try { [StatusLine.StatusBar]::Configure(@{ Padding = 'bad' }) }
                catch { $rejected = $true }
                [pscustomobject]@{ PromptAbsent = $promptAbsent; BarAbsent = $barAbsent; Rejected = $rejected }
            }).AddArgument($modulePath)
            $result = $other.Invoke()[0]
            $result.PromptAbsent | Should -BeTrue
            $result.BarAbsent | Should -BeTrue
            $result.Rejected | Should -BeTrue
        }
        finally { $other.Dispose() }
    }
}

Describe 'Host-writable runtime updates' -Tag Contract {
    AfterEach { Disable-StatusLine -Force }

    It 'applies UpdateDuringCommand to the existing worker' {
        $originalOutput = [Console]::Out
        $writer = [IO.StringWriter]::new()
        $definition = New-StatusLineBlock -ScriptBlock { [datetime]::Now.Ticks } -IntervalMilliseconds 250 -Alignment Left
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            Enable-StatusLine -Block $definition -Target HostWritable -Force -IntervalMilliseconds 100 -IdleTimeoutSeconds 0 -Style Plain -Padding '' -UpdateDuringCommand:$false
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('[2K') }
            $owner = [runspace]::DefaultRunspace.InstanceId
            [StatusLine.StatusLineActivity]::EndReadLine($owner)
            [StatusLine.StatusLineActivity]::CommandAccepted($owner, 'long command')
            $before = $writer.ToString()
            Start-Sleep -Milliseconds 350
            $writer.ToString() | Should -BeExactly $before
            [StatusLine.StatusBar]::Configure(@{ UpdateDuringCommand = $true })
            Wait-RefreshCondition { $writer.ToString().Length -gt $before.Length }
        }
        finally { Disable-StatusLine -Force; [Console]::SetOut($originalOutput); $definition.Dispose(); $writer.Dispose() }
    }

    It 'discards a pending frame when configuration changes' {
        $originalOutput = [Console]::Out
        $writer = [IO.StringWriter]::new()
        # Unchanged frames are never queued, so the old block must keep changing.
        $definition = New-StatusLineBlock -ScriptBlock { "OLD$([datetime]::Now.Ticks)" } -IntervalMilliseconds 250 -Alignment Left
        try {
            [Console]::SetOut([IO.TextWriter]::Synchronized($writer))
            Enable-StatusLine -Block $definition -Target HostWritable -Force -IntervalMilliseconds 100 -IdleTimeoutSeconds 0 -Style Plain -Padding ''
            Wait-RefreshCondition -FlushHost { $writer.ToString().Contains('OLD') }
            $output = $updaterType.GetField('currentOutput', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
            Set-TestHostDeferral -Deferred
            $pending = $output.GetType().GetField('hostPending', [Reflection.BindingFlags]'Instance,NonPublic')
            Wait-RefreshCondition { $pending.GetValue($output) }
            $before = $writer.ToString().Length
            [StatusLine.StatusBar]::SetBlocks('Block', @([StatusLine.BlockSnapshots]::Create('NEW')))
            [StatusLine.StatusLineActivity]::FlushHost([runspace]::DefaultRunspace.InstanceId)
            $writer.ToString().Substring($before) | Should -Not -Match 'OLD'
            Wait-RefreshCondition {
                [StatusLine.StatusLineActivity]::FlushHost([runspace]::DefaultRunspace.InstanceId)
                $writer.ToString().Substring($before).Contains('NEW')
            }
        }
        finally { Disable-StatusLine -Force; [Console]::SetOut($originalOutput); $definition.Dispose(); $writer.Dispose() }
    }
}
