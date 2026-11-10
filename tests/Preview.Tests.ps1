BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Preview commands' -Tag Contract {
    It 'separates displayed directory components when configured' {
        $directory = Join-Path $TestDrive 'parent/child'
        $null = New-Item -ItemType Directory -Path $directory -Force
        $previousStyle = [StatusLine.DirectoryDisplay]::Style
        $previousBase = [StatusLine.DirectoryDisplay]::BasePath
        Push-Location -LiteralPath $directory
        try {
            [StatusLine.DirectoryDisplay]::Style = 'Relative'
            [StatusLine.DirectoryDisplay]::BasePath = $TestDrive
            $configuration = New-TestPromptConfiguration @{
                Left = @('Directory'); Right = @(); Style = 'Plain'
                IconSet = 'None'; DirectorySeparator = ' › '
            }
            $sequence = Show-Prompt -Configuration $configuration -Width 160
            ($sequence -replace '\x1b\[[0-9;]*m') | Should -Match '^ *parent › child'
            $configuration = New-TestPromptConfiguration @{
                Left = @('Directory'); Right = @(); Style = 'Plain'
                IconSet = 'NerdFont'; DirectorySeparator = ' › '
            }
            $sequence = Show-Prompt -Configuration $configuration -Width 160
            ($sequence -replace '\x1b\[[0-9;]*m') | Should -Match '^ *\uF07C › parent › child'
        }
        finally {
            [StatusLine.DirectoryDisplay]::Style = $previousStyle
            [StatusLine.DirectoryDisplay]::BasePath = $previousBase
            Pop-Location
        }
    }

    It 'colors the prompt symbol for exit code <Code>' -ForEach @(
        @{ Code = 0; Color = 92 }
        @{ Code = 1; Color = 91 }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Left = @(); Right = @(); Rows = 2; ConnectRows = $true
            InputLinePrefix = 'corner '; Symbol = '>> '; TransientSymbol = '>> '
            ColorSymbolByExitCode = $true
        }
        $expected = [regex]::Escape("${escape}[0;${Color};49m>> ${escape}[0m")
        Show-Prompt -Configuration $configuration -ExitCode $Code | Should -Match $expected
        Show-Prompt -Configuration $configuration -ExitCode $Code -Transient | Should -Match $expected
    }

    It 'renders a prompt without replacing installed functions or starting a statusline' {
        $previousPrompt = $function:global:prompt
        $previousReader = $function:global:PSConsoleHostReadLine
        $configuration = New-TestPromptConfiguration @{
            Left = @([StatusLine.BlockSnapshots]::Create('PREVIEW')); Right = @()
            Style = 'Plain'; Padding = ''; Symbol = '> '; ShellIntegration = 'Osc133'
        }
        $sequence = Show-Prompt -Configuration $configuration -Width 40
        ($sequence -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'PREVIEW> '
        $sequence | Should -Not -Match '\x1b\]133;'
        $function:global:prompt | Should -Be $previousPrompt
        $function:global:PSConsoleHostReadLine | Should -Be $previousReader
        [StatusLine.Prompt]::Configuration | Should -BeNullOrEmpty
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
    }

    It 'renders a status row without selecting a terminal display or creating an output file' {
        $path = Join-Path $TestDrive 'preview-output'
        $configuration = [StatusLine.StatusBarConfiguration]@{
            Left = @([StatusLine.BlockSnapshots]::Create('ROW')); Style = 'Plain'; Padding = ''
            Target = 'File'; OutputPath = $path
        }
        $sequence = Show-StatusLine -Configuration $configuration -Width 20
        ($sequence -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'ROW'
        $sequence | Should -Not -Match '\x1b\[[0-9]+\$[}~]'
        Test-Path -LiteralPath $path | Should -BeFalse
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
    }

    It 'includes script block output in the first <Command> call' -ForEach @(
        @{ Command = 'Show-Prompt' }
        @{ Command = 'Show-StatusLine' }
    ) {
        $definition = New-StatusLineBlock -Name "FirstPreview$Command" -ScriptBlock {
            param($state)
            Start-Sleep -Milliseconds 100
            "exit=$($state.LastExitCode)"
        }
        try {
            $sequence = & $Command -Configuration @{ Left = @($definition); Right = @(); Style = 'Plain'; Padding = '' } -ExitCode 3 -Width 40
            ($sequence -replace '\x1b\[[0-9;]*m') | Should -Match 'exit=3'
        }
        finally {
            $definition.Dispose()
        }
    }

    It 'rejects invalid <Command> width before rendering' -ForEach @(
        @{ Command = 'Show-Prompt'; Width = 1 }
        @{ Command = 'Show-StatusLine'; Width = 0 }
    ) {
        $caught = $null
        try { & $Command -Width $Width -ErrorAction Stop }
        catch { $caught = $_ }
        $caught.FullyQualifiedErrorId | Should -BeLike 'ParameterArgumentValidationError*'
    }

    It 'rejects invalid status configuration without starting a worker' {
        { Show-StatusLine -Configuration ([StatusLine.StatusBarConfiguration]@{ BlockTimeoutMilliseconds = 0 }) } |
            Should -Throw
        [StatusLine.StatusBar]::Configuration | Should -BeNullOrEmpty
    }
}
