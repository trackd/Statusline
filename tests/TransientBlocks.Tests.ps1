BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Cached transient blocks' -Tag Contract {
    AfterEach { Disable-Prompt }

    It 'uses the replaced prompts duration and errors rather than the accepted commands values' {
        $configuration = New-TestPromptConfiguration @{
            Transient = $true; TransientBlock = @('Duration', 'Errors')
            Left = @(); Right = @(); Input = @()
            Style = 'Plain'; IconSet = 'None'; Padding = ''
            TransientSymbol = '> '; Symbol = '> '; ShellIntegration = 'None'
        }
        Enable-Prompt -Configuration $configuration
        $runspace = [runspace]::DefaultRunspace
        $start = [datetime]::Now.AddMinutes(-1)
        $history = Add-History -InputObject ([pscustomobject]@{
            CommandLine = 'previous'; ExecutionStatus = 'Completed'
            StartExecutionTime = $start; EndExecutionTime = $start.AddMilliseconds(42)
        }) -PassThru
        try {
            $location = $ExecutionContext.SessionState.Path.CurrentLocation
            $null = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $history, 3)
            [StatusLine.PromptSession]::BeginTransient($runspace)
            $text = [StatusLine.PromptSession]::Render($runspace, $false, 9, $location, $null, 99)
            $plain = $text -replace '\x1b\[[0-9;]*m'
            $plain | Should -Match '42ms'
            $plain | Should -Match '3'
            $plain | Should -Not -Match '99'
            $again = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 100)
            $again | Should -BeExactly $text
            [StatusLine.PromptSession]::EndTransient($runspace)
            $history = Add-History -InputObject ([pscustomobject]@{
                CommandLine = 'next'; ExecutionStatus = 'Completed'
                StartExecutionTime = $start; EndExecutionTime = $start.AddSeconds(2)
            }) -PassThru
            $null = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $history, 0)
            [StatusLine.PromptSession]::BeginTransient($runspace)
            $updated = [StatusLine.PromptSession]::Render($runspace, $true, 0, $location, $null, 99)
            ($updated -replace '\x1b\[[0-9;]*m') | Should -Match '2.0s'
            $updated | Should -Not -Be $text
        }
        finally {
            [StatusLine.PromptSession]::EndTransient($runspace)
            # These history entries belong only to this isolated test process.
        }
    }

    It 'does not refresh a script provider during a transient redraw' {
        $marker = Join-Path $TestDrive 'transient-value'
        Set-Content -LiteralPath $marker 'original'
        $scriptText = "Get-Content -LiteralPath '" + $marker.Replace("'", "''") + "'"
        $transientValue = New-StatusLineBlock -Name transientValue -ScriptBlock ([scriptblock]::Create($scriptText)) -IntervalMilliseconds 250
        try {
            $configuration = New-TestPromptConfiguration @{
                Transient = $true; TransientBlock = @()
                Left = @(); Right = @(); Input = @()
                Style = 'Plain'; IconSet = 'None'; Padding = ''
                TransientSymbol = '> '; Symbol = '> '; ShellIntegration = 'None'
            }
            Enable-Prompt -Configuration $configuration -TransientBlock $transientValue
            $null = & prompt
            Start-Sleep -Milliseconds 500
            $null = & prompt
            Set-Content -LiteralPath $marker 'changed'
            Start-Sleep -Milliseconds 350
            [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
            $first = & prompt
            ($first -replace '\x1b\[[0-9;]*m') | Should -Match 'original'
            (& prompt) | Should -BeExactly $first
        }
        finally {
            [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
            $transientValue.Dispose()
        }
    }

    It 'keeps transient blocks opt-in and accepts an explicit empty override' {
        $configuration = New-TestPromptConfiguration @{
            Transient = $true; TransientBlock = @('Duration')
            Left = @(); Right = @(); Input = @()
            Style = 'Plain'; IconSet = 'None'; Padding = ''
            TransientSymbol = 'tiny> '; Symbol = '> '; ShellIntegration = 'None'
        }
        Enable-Prompt -Configuration $configuration -TransientBlock @()
        $null = & prompt
        [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
        ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'tiny> '
    }
}

Describe 'Implicit transient enablement' -Tag Contract {
    AfterEach { Disable-Prompt }

    It 'enables replacement from a block selection unless explicitly disabled: <Disabled>' -ForEach @(
        @{ Disabled = $false }
        @{ Disabled = $true }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Transient = $false; TransientBlock = @()
            Left = @(); Right = @(); Input = @()
            Rows = 1; Style = 'Plain'; IconSet = 'None'; Padding = ''
            ShellIntegration = 'None'; TransientSymbol = '> '
        }
        $options = @{
            Configuration = $configuration
            TransientBlock = @('Duration')
        }
        if ($Disabled) { $options.Transient = $false }
        Enable-Prompt @options
        $row = [StatusLine.PromptSession]::GetTransientRow([runspace]::DefaultRunspace, 'abc', 4, 80, 10, '>> ')
        if ($Disabled) { $row | Should -BeNullOrEmpty }
        else { $row | Should -Be 9 }
    }
}
