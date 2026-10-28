BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Public measurement contract' -Tag Contract {
    It 'binds the escape switch and distinguishes included from skipped sequences' {
        (Get-Command Measure-Codepoint).Parameters.ContainsKey('IncludeEscSequences') | Should -BeTrue
        $text = "`e[31mX`e[0m"
        $skipped = Measure-Codepoint -InputString $text
        $included = Measure-Codepoint -InputString $text -IncludeEscSequences
        $skipped.Columns | Should -Be 1
        $skipped.ContainsVT | Should -BeTrue
        $included.ContainsVT | Should -BeFalse
        $included.Columns | Should -BeGreaterThan $skipped.Columns
    }

    It 'streams empty, single and wide input as measurement objects' {
        $values = @('', 'A', '日本') | Measure-Codepoint
        $values.Count | Should -Be 3
        $values.Columns | Should -Be @(0, 1, 4)
        $values[0].GetType() | Should -Be ([StatusLine.Unicode+MeasurementResult])
    }

    It 'does not expose the unshipped extension target' {
        $path = Join-Path $TestDrive 'disabled-extension'
        [Enum]::GetNames([StatusLine.StatusLineTarget]) | Should -Be @('HostWritable', 'File')
        { Enable-StatusLine -Target Extension -Force -OutputPath $path -ErrorAction Stop } | Should -Throw
        Test-Path -LiteralPath $path | Should -BeFalse
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
    }
}

Describe 'Command validation and mutation boundaries' -Tag Contract {
    BeforeEach { $previousDepth = [StatusLine.DirectoryDisplay]::MaximumComponents }
    AfterEach {
        Disable-Prompt
        Disable-StatusLine
        [StatusLine.DirectoryDisplay]::MaximumComponents = $previousDepth
    }

    It 'rejects undefined enum arguments during parameter binding' {
        { Enable-StatusLine -Style 999 -ErrorAction Stop } |
            Should -Throw -ErrorId 'CannotConvertArgumentNoMessage,StatusLine.Commands.EnableStatusLineCommand'
        { Disable-StatusLine -Mode 999 -ErrorAction Stop } |
            Should -Throw -ErrorId 'CannotConvertArgumentNoMessage,StatusLine.Commands.DisableStatusLineCommand'
        { New-StatusLineBlock -Kind Clock -Alignment 999 -ErrorAction Stop } |
            Should -Throw -ErrorId 'CannotConvertArgumentNoMessage,StatusLine.Commands.NewStatusLineBlockCommand'
        [StatusLine.Prompt]::Configuration | Should -BeNullOrEmpty
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be $previousDepth
    }

    It 'rejects a null prompt configuration at binding' {
        { Enable-Prompt -Configuration $null -ErrorAction Stop } | Should -Throw -ErrorId 'ParameterArgumentValidationError,StatusLine.Commands.EnablePromptCommand'
    }

    It 'does not change directory settings when prompt validation fails' {
        $configuration = New-TestPromptConfiguration @{ Left = @('NotABlock') }
        { Enable-Prompt -Configuration $configuration -DirectoryDepth 2 -ErrorAction Stop } | Should -Throw -ErrorId 'InvalidPromptConfiguration,StatusLine.Commands.EnablePromptCommand'
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be $previousDepth
    }

    It 'rejects non-filesystem output before starting a worker or creating a file' {
        $name = 'StatusLineProviderProbe-' + [guid]::NewGuid().ToString('N')
        $path = Join-Path $TestDrive $name
        Push-Location $TestDrive
        try {
            { Enable-StatusLine -Target File -OutputPath "Env:\$name" -DirectoryDepth 2 -ErrorAction Stop } |
                Should -Throw -ErrorId 'StatusLineFileSystemRequired,StatusLine.Commands.EnableStatusLineCommand'
            Test-Path -LiteralPath $path | Should -BeFalse
            [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
            [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be $previousDepth
        }
        finally { Pop-Location }
    }

    It 'does not install, write or change settings under WhatIf' {
        $path = Join-Path $TestDrive 'whatif-status'
        $prompt = $function:global:prompt
        Enable-Prompt -DirectoryDepth 2 -WhatIf
        Enable-StatusLine -Target File -OutputPath $path -DirectoryDepth 2 -WhatIf
        $function:global:prompt | Should -Be $prompt
        [StatusLine.Prompt]::Configuration | Should -BeNullOrEmpty
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
        Test-Path -LiteralPath $path | Should -BeFalse
        [StatusLine.DirectoryDisplay]::MaximumComponents | Should -Be $previousDepth
    }

    It 'does not disable existing surfaces under WhatIf' {
        $path = Join-Path $TestDrive 'keep-status'
        Enable-Prompt -Configuration (New-TestPromptConfiguration)
        Enable-StatusLine -Target File -OutputPath $path -Block Clock
        Disable-Prompt -WhatIf
        Disable-StatusLine -WhatIf
        [StatusLine.Prompt]::Configuration | Should -Not -BeNullOrEmpty
        [StatusLine.StatusBar]::Configuration | Should -Not -BeNullOrEmpty
    }
}

Describe 'Live settings views' -Tag Contract {
    AfterEach {
        Disable-StatusLine
        Disable-Prompt
    }

    It 'applies prompt property edits while retaining detached snapshots and hook ownership' {
        $first = [StatusLine.BlockSnapshots]::Create('FIRST')
        Enable-Prompt -Configuration (New-TestPromptConfiguration @{
            Left = @($first); Right = @(); Rows = 1; Style = 'Plain'; Padding = ''; Symbol = '> '
        })
        $hook = $function:global:prompt
        $settings = [StatusLine.Prompt]::Settings
        $settings.Left.GetType() | Should -Be ([object[]])
        $settings.Left[0] | Should -Be $first
        $settings.Symbol = 'live> '
        ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'FIRSTlive> '
        $settings.Left = @([StatusLine.BlockSnapshots]::Create('NEXT'))
        ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'NEXTlive> '
        $snapshot = [StatusLine.Prompt]::Configuration
        $snapshot.Left[0] = $first
        [StatusLine.Prompt]::Configuration.Left[0].Text | Should -Be 'NEXT'
        $function:global:prompt | Should -Be $hook
        { $settings.Rows = 3 } | Should -Throw
        [StatusLine.Prompt]::Configuration.Rows | Should -Be 1
        $settings.PSObject.Properties['ShellIntegration'].IsSettable | Should -BeFalse
    }

    It 'changes statusline properties through the existing worker and rejects an invalid edit atomically' {
        $path = Join-Path $TestDrive 'live-settings'
        Enable-StatusLine -Target File -OutputPath $path -Block Clock -IdleTimeoutSeconds 0 -Style Plain -IconSet None
        $settings = [StatusLine.StatusBar]::Settings
        $settings.Padding = '.'
        $settings.Left = @([StatusLine.BlockSnapshots]::Create('LIVE'))
        Wait-RefreshCondition { (Test-Path $path) -and ((Read-TestStatusFile $path) -replace '\x1b\[[0-9;]*m').Contains('.LIVE.') }
        { $settings.IntervalMilliseconds = 1 } | Should -Throw
        [StatusLine.StatusBar]::Configuration.IntervalMilliseconds | Should -Be 1000
        $settings.PSObject.Properties['Target'].IsSettable | Should -BeFalse
        Disable-StatusLine
        $settings.Padding | Should -BeNullOrEmpty
        { $settings.Padding = 'disabled' } | Should -Throw
    }
}
