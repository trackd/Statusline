BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Reusable block definitions' -Tag Provider {
    AfterEach {
        Disable-Prompt
        Disable-StatusLine
    }

    It 'constructs scripts lazily and preserves independent definitions with the same name' {
        $marker = Join-Path $TestDrive 'not-run'
        $script = [scriptblock]::Create("Set-Content -LiteralPath '$marker' -Value invoked; 42")
        $first = New-StatusLineBlock -Name Example -ScriptBlock $script -Foreground ([ConsoleColor]::Cyan)
        $second = New-StatusLineBlock -Name Example -ScriptBlock { 99 }
        try {
            Test-Path $marker | Should -BeFalse
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($first)).LastUpdated | Should -BeNullOrEmpty
            (New-TestBlock -Kind $first).Text | Should -Be '42'
            (New-TestBlock -Kind $second).Text | Should -Be '99'
            (New-TestBlock -Kind $first).Foreground.Palette | Should -Be ([ConsoleColor]::Cyan)
        }
        finally { $first.Dispose(); $second.Dispose() }
    }

    It 'reuses builtin kinds with explicit per-block styles and dynamic session values' {
        $definition = New-StatusLineBlock -Kind Errors -Foreground '#123456' -Background '#ABCDEF' -Icon '!' -Padding '.' -Bold
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @($definition); State = [StatusLine.StatusLineState]@{ ErrorCount = 3 }
            Padding = ''; IconSet = 'None'; StartColor = '#000000'; EndColor = '#FFFFFF'
        }
        $first = [StatusLine.BlockSnapshots]::Snapshot($options)[0]
        $first.Text | Should -Be '3'
        $first.Foreground.Rgb.ToArgb() | Should -Be ([Drawing.Color]::FromArgb(18, 52, 86).ToArgb())
        $first.Background.Rgb.ToArgb() | Should -Be ([Drawing.Color]::FromArgb(171, 205, 239).ToArgb())
        $first.Icon | Should -Be '!'
        $first.Padding | Should -Be '.'
        $first.Bold | Should -BeTrue
        $options = [StatusLine.BlockSnapshotOptions]@{ Block = @($definition); State = [StatusLine.StatusLineState]@{ ErrorCount = 7 }; Padding = ''; IconSet = 'None' }
        [StatusLine.BlockSnapshots]::Snapshot($options)[0].Text | Should -Be '7'
    }

    It 'accepts the same definition directly in prompt and statusline blocks' {
        $definition = New-StatusLineBlock -ScriptBlock { 'shared' } -Alignment Left
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Input = @(); Block = @()
            Style = 'Plain'; IconSet = 'None'; Padding = ''
            Symbol = '> '; Rows = 1; ShellIntegration = 'None'; Transient = $false
        }
        $path = Join-Path $TestDrive 'shared-definition'
        try {
            $null = New-TestBlock -Kind $definition
            Enable-Prompt -Configuration $configuration -Block $definition
            ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'shared> '
            Enable-StatusLine -Block $definition -Target File -OutputPath $path -Style Plain -Padding '' -IntervalMilliseconds 100
            Wait-RefreshCondition { (Test-Path $path) -and (Get-Content $path -Raw).Contains('shared') }
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($definition)).LastUpdated | Should -Not -BeNullOrEmpty
            $last = ([StatusLine.RefreshDiagnostics]::GetScriptStatus($definition)).LastUpdated
            $null = & prompt
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($definition)).LastUpdated | Should -Be $last
        }
        finally { Disable-Prompt; Disable-StatusLine; $definition.Dispose() }
    }

    It 'honors explicit colors in filled rendering even with global gradients and automatic contrast' {
        $definition = New-StatusLineBlock -Kind Errors -Foreground '#123456' -Background '#ABCDEF'
        $options = [StatusLine.BlockSnapshotOptions]@{ Block = @($definition); State = [StatusLine.StatusLineState]@{ ErrorCount = 2 }; Padding = ''; IconSet = 'None' }
        $blocks = [StatusLine.BlockSnapshots]::Snapshot($options)
        $sequence = [StatusLine.TerminalControl]::BlocksSequence($blocks, [StatusLine.BlockRenderOptions]@{
            Width = 30; Style = 'Powerline'; BlendBackground = $true
            StartColor = '#000000'; EndColor = '#FFFFFF'; TextContrast = 'Automatic'
        })
        $sequence | Should -Match '38;2;18;52;86;48;2;171;205;239'
    }
}
