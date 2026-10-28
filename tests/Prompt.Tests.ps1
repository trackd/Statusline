BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Prompt rendering and lifecycle' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'connects both prompt rows with <Label>' -ForEach @(
        @{ Label = 'an explicit input symbol'; InputSymbol = '❯ '; ExpectedSymbol = '❯ ' }
        @{ Label = 'the transient symbol for an empty input row'; InputSymbol = ' '; ExpectedSymbol = 'compact> ' }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Style = 'Plain'
            Padding = ''
            ContextSymbol = '╭─ '
            InputLinePrefix = '╰─ '
            Symbol = $InputSymbol
            TransientSymbol = 'compact> '
            Rows = 2
        }
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        $text = (Get-TestPrompt -Configuration $configuration -Block $blocks -Width 20) -replace '\x1b\[[0-9;]*m'
        $text | Should -BeExactly "╭─ LEFT       RIGHT`n╰─ $ExpectedSymbol"
    }

    It 'renders reusable blocks on the input row with independently configured markers' {
        $configuration = (New-TestPromptConfiguration @{
            ContextSymbol = 'top: '
            InputLinePrefix = 'input: '
            Symbol = '> '
            Left = @()
            Right = @()
            Input = @([StatusLine.BlockSnapshots]::Create('SECONDARY'))
            Style = 'Plain'
            Padding = ''
        })
        $text = (Get-TestPrompt -Configuration $configuration -Block @(New-TestBlock 'CONTEXT') -Width 40) -replace '\x1b\[[0-9;]*m'
        $text | Should -BeExactly "top: CONTEXT`ninput: SECONDARY> "
    }

    It 'allows input row kinds without right context and overrides them through Enable-Prompt' {
        $previousPrompt = $function:global:prompt
        try {
            $configuration = (New-TestPromptConfiguration @{
                ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '
                Left = @(); Right = @(); Input = @('Clock')
                Style = 'Plain'; Padding = ''; Transient = $false; ShellIntegration = 'None'
            })
            Enable-Prompt -Configuration $configuration -Input @([StatusLine.BlockSnapshots]::Create('custom'))
            ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly "`ncustom> "
        }
        finally { Disable-Prompt; $function:global:prompt = $previousPrompt }
    }

    It 'does not inherit a right prompt when only the left group is selected' {
        $previousPrompt = $function:global:prompt
        try {
            Enable-Prompt -Left Errors -Transient:$false -ShellIntegration None
            $sessions = [StatusLine.PromptSession].GetField('Sessions', [Reflection.BindingFlags]'Static,NonPublic').GetValue($null)
            $session = $sessions[[runspace]::DefaultRunspace.InstanceId]
            $layout = [StatusLine.PromptSession].GetField('layout', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($session)
            $layout.Configuration.Right.Count | Should -Be 0
        }
        finally { Disable-Prompt; $function:global:prompt = $previousPrompt }
    }

    It 'redraws blank accepted input transiently' -ForEach @('','   ') {
        $previousPrompt = $function:global:prompt
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  TransientSymbol = 'compact> '; Left = @(); Right = @(); Transient = $false })) -ShellIntegration None
            [StatusLine.PromptSession]::TryBeginTransient([runspace]::DefaultRunspace, $_) | Should -BeTrue
            ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly 'compact> '
        }
        finally {
            [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
            Disable-Prompt
            $function:global:prompt = $previousPrompt
        }
    }

    It 'retains the session error count across successful prompt redraws' {
        $previousPrompt = $function:global:prompt
        $record = [Management.Automation.ErrorRecord]::new([Exception]::new('retained'), 'RetainedTest', 'NotSpecified', $null)
        $global:Error.Insert(0, $record)
        try {
            $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Left = @('Errors'); Right = @(); Style = 'Plain'; IconSet = 'None'; Padding = ''; Symbol = '> '; Transient = $false })
            Enable-Prompt -Configuration $configuration -ShellIntegration None
            $expectedCount = $global:Error.Count
            foreach ($redraw in 1..2) {
                $true | Out-Null
                ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly "ERR $expectedCount> "
            }
        }
        finally { Disable-Prompt; $global:Error.Remove($record); $function:global:prompt = $previousPrompt }
    }

    It 'renders right blocks on a context row without absolute cursor movements' {
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; TransientSymbol = '> '; Transient = $false; 
            Style = 'Plain';
            Padding = '';
            Symbol = '> ';
            Rows = 2;
            })
        $text = Get-TestPrompt -Configuration $configuration -Block $blocks -Width 20
        ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly "LEFT          RIGHT`n> "
        $text | Should -Not -Match '\x1b\[[0-9;]+H'
    }

    It 'forces right blocks onto a context row even in single-line mode' {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; TransientSymbol = '> '; Transient = $false;  Style = 'Plain'; Symbol = '> ' })
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        $text = Get-TestPrompt -Configuration $configuration -Block $blocks -Width 40
        ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly "LEFT                              RIGHT`n> "
        $text | Should -Not -Match '\x1b\[[0-9]+G'
    }

    It 'fits a long directory and crowded <Label> inside the terminal width' -ForEach @(
        @{ Label = 'single-row prompt'; Settings = @{ Rows = 1; Left = @('Directory', 'User', 'Host', 'PowerShell', 'Os'); Right = @() } }
        @{ Label = 'left and right context row'; Settings = @{ Rows = 2; Left = @('Directory', 'User'); Right = @('Host', 'PowerShell', 'Os') } }
    ) {
        $long = Join-Path $TestDrive ('averyverylongfoldername' * 6)
        New-Item -ItemType Directory -Path $long -Force | Out-Null
        Push-Location -LiteralPath $long
        try {
            $settings = @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false; Style = 'Powerline' } + $Settings
            $configuration = New-TestPromptConfiguration $settings
            foreach ($width in 40, 80, 160) {
                $lines = (Get-TestPrompt -Configuration $configuration -Width $width) -split "`n"
                foreach ($line in $lines) {
                    (Measure-Codepoint -InputString $line).Columns | Should -BeLessOrEqual ($width - 1)
                }
                ($lines[0] -replace '\x1b\[[0-9;]*m') | Should -Match '…'
                if ($Settings.Rows -eq 1) {
                    ($width - 1 - (Measure-Codepoint -InputString $lines[-1]).Columns) | Should -BeGreaterOrEqual ([math]::Min(20, [math]::Floor(($width - 1) / 2)))
                }
            }
        }
        finally {
            Pop-Location
        }
    }

    It 'does not split wide graphemes in a narrow prompt' {
        $blocks = @(New-TestBlock ([string][char]0x65E5 + [char]0x672C))
        $text = Get-TestPrompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Style = 'Plain'; Rows = 2 })) -Block $blocks -Width 4
        ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly "$([char]0x65E5)`n> "
    }

    It 'renders a compact transient prompt without querying providers' {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; Transient = $false; Left = @('Weather'); TransientSymbol = '$ '; StartColor = '#61AFEF'; EndColor = '#61AFEF' })
        $text = Get-TestPrompt -Configuration $configuration -Transient
        ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly '$ '
        $text | Should -Match '38;2;97;175;239'
    }

    It 'colors transient text with the configured theme without filling its background' {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  StartColor = '#123456'; EndColor = '#ABCDEF'; TransientSymbol = '> ' })
        $text = Get-TestPrompt -Configuration $configuration -Transient
        $text | Should -BeExactly "$escape[0;38;2;18;52;86;49m> $escape[0m"
    }

    It 'inherits the last normal prompt accent without querying providers' {
        $previousPrompt = $function:global:prompt
        try {
            Enable-Prompt -Configuration (New-TestPromptConfiguration) -Block (New-TestBlock 'theme' -Foreground Coral) -Left @() -Right @() -Transient:$false -ShellIntegration None
            $null = & prompt
            [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
            (& prompt) | Should -Match '38;2;255;127;80;49m'
        }
        finally { [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace); Disable-Prompt; $function:global:prompt = $previousPrompt }
    }

    It 'removes control sequences from block text' {
        $block = New-TestBlock "hi$escape]2;injected`a$escape[2Jthere"
        $text = Get-TestPrompt -Block $block
        $text | Should -Not -Match 'injected|\x1b\]2|\x1b\[2J'
    }

    It 'restores the previous prompt after disabling' {
        $original = $function:global:prompt
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -Left Directory -Right Clock -ShellIntegration None
            (& prompt) | Should -Match '> '
            Disable-Prompt
            $function:global:prompt | Should -Be $original
        }
        finally { Disable-Prompt; $function:global:prompt = $original }
    }

    It 'does not overwrite a prompt installed by another tool after enable' {
        $original = $function:global:prompt
        $replacement = { 'replacement> ' }
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -ShellIntegration None
            $function:global:prompt = $replacement
            Disable-Prompt
            $function:global:prompt | Should -Be $replacement
        }
        finally { $function:global:prompt = $original }
    }

    It 'uses a ReadLine wrapper without changing Enter and restores the extra line count' {
        Import-Module PSReadLine
        $original = $function:global:prompt
        $enter = Get-PSReadLineKeyHandler -Chord Enter
        $readLine = $function:global:PSConsoleHostReadLine
        $count = (Get-PSReadLineOption).ExtraPromptLineCount
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -Transient -ShellIntegration None
            (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -Be $enter.Function
            $function:global:PSConsoleHostReadLine | Should -Not -Be $readLine
            Disable-Prompt
            (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -Be $enter.Function
            (Get-PSReadLineOption).ExtraPromptLineCount | Should -Be $count
            $function:global:PSConsoleHostReadLine | Should -Be $readLine
        }
        finally { Disable-Prompt; $function:global:prompt = $original }
    }

    It 'redraws transiently without changing native exit status' {
        $original = $function:global:prompt
        $native = $global:LASTEXITCODE
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -Transient -ShellIntegration None
            $global:LASTEXITCODE = 5
            [StatusLine.PromptSession]::BeginTransient([runspace]::DefaultRunspace)
            ((& prompt) -replace '\x1b\[[0-9;]*m') | Should -BeExactly '> '
            $global:LASTEXITCODE | Should -Be 5
        }
        finally {
            [StatusLine.PromptSession]::EndTransient([runspace]::DefaultRunspace)
            Disable-Prompt
            $global:LASTEXITCODE = $native
            $function:global:prompt = $original
        }
    }

    It 'places a registered right-aligned block in the requested left prompt group' {
        $promptShared = New-StatusLineBlock -Name promptShared -ScriptBlock { 'shared' } -Alignment Right
        try {
            New-TestBlock -Kind $promptShared | Out-Null
            $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Left = @($promptShared); Right = @(); Style = 'Plain'; Padding = ''; Rows = 2 })
            ((Get-TestPrompt -Configuration $configuration -Width 30) -replace '\x1b\[[0-9;]*m') | Should -BeExactly "shared`n> "
        }
        finally { $promptShared.Dispose() }
    }

    It 'preserves a custom Enter handler during transient enable and disable' {
        Import-Module PSReadLine
        $original = Get-PSReadLineKeyHandler -Chord Enter
        $originalPrompt = $function:global:prompt
        try {
            Set-PSReadLineKeyHandler -Chord Enter -BriefDescription TestCustomEnter -ScriptBlock { param($key, $arg) }
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -Transient -ShellIntegration None
            (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -Be 'TestCustomEnter'
            Disable-Prompt
            (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -Be 'TestCustomEnter'
        }
        finally {
            Disable-Prompt
            Set-PSReadLineKeyHandler -Chord Enter -Function $original.Function
            $function:global:prompt = $originalPrompt
        }
    }

    It 'clips combining characters as one grapheme after an ASCII run' {
        $block = New-TestBlock ("ab" + [char]0x0301 + 'c')
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Style = 'Plain'; Rows = 2 })
        ((Get-TestPrompt -Block $block -Configuration $configuration -Width 3) -replace '\x1b\[[0-9;]*m') | Should -BeExactly "ab$([char]0x0301)`n> "
    }

    It 'keeps the input row clear despite a legacy InputLine setting' {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Rows = 2; Style = 'Plain'; })
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        $text = (Get-TestPrompt -Configuration $configuration -Block $blocks -Width 40) -replace '\x1b\[[0-9;]*m'
        $text | Should -BeExactly "LEFT                              RIGHT`n> "
    }

    It 'drops right content rather than joining it to the left group in a narrow terminal' {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Style = 'Plain' })
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        (Get-TestPrompt -Configuration $configuration -Block $blocks -Width 10) -replace '\x1b\[[0-9;]*m' | Should -BeExactly "LEFT`n> "
        (Get-TestPrompt -Configuration $configuration -Block $blocks -Width 11) -replace '\x1b\[[0-9;]*m' | Should -BeExactly "LEFT RIGHT`n> "
    }

    It 'rejects a gradient with only one endpoint' {
        { New-TestBlock -Kind User -StartColor Black } | Should -Throw
        { Enable-StatusLine -StartColor Black -Target File } | Should -Throw
        { Get-TestPrompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  StartColor = '#000000' })) } | Should -Throw
    }

    It 'supports <Label> gradient endpoints in prompt configuration' -ForEach @(
        @{ Label = 'hex'; Start = '#000000'; End = '#FFFFFF' }
        @{ Label = 'named'; Start = 'Black'; End = 'White' }
        @{ Label = 'Color object'; Start = [Drawing.Color]::Black; End = [Drawing.Color]::White }
    ) {
        $configuration = (New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false; StartColor = $Start; EndColor = $End; Style = 'Powerline' })
        $blocks = @(New-TestBlock 'one'; New-TestBlock 'two')
        $text = Get-TestPrompt -Configuration $configuration -Block $blocks
        $text | Should -Match '48;2;0;0;0m'
        $text | Should -Match '48;2;255;255;255m'
    }

    It 'rejects prompt row counts outside one or two' {
        { Enable-Prompt -Rows 3 } | Should -Throw
        { Get-TestPrompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '; Transient = $false;  Rows = 0 })) } | Should -Throw
    }

    It 'does not overwrite a ReadLine function replaced after enable' {
        Import-Module PSReadLine
        $original = $function:global:PSConsoleHostReadLine
        $prompt = $function:global:prompt
        $replacement = { 'replacement' }
        try {
            Enable-Prompt -Configuration ((New-TestPromptConfiguration @{ ContextSymbol = ''; InputLinePrefix = '';  Symbol = '> '; TransientSymbol = '> '; Transient = $false })) -Transient -ShellIntegration None
            $function:global:PSConsoleHostReadLine = $replacement
            Disable-Prompt
            $function:global:PSConsoleHostReadLine | Should -Be $replacement
        }
        finally { Disable-Prompt; $function:global:PSConsoleHostReadLine = $original; $function:global:prompt = $prompt }
    }
}
