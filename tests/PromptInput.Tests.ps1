BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    Import-Module PSReadLine
}

Describe 'Prompt input hook ownership' -Tag Contract {
    BeforeEach {
        $originalPrompt = $function:global:prompt
        $originalReader = $function:global:PSConsoleHostReadLine
        $originalPromptText = (Get-PSReadLineOption).PromptText
        $originalMode = (Get-PSReadLineOption).EditMode
        $originalExtraLines = (Get-PSReadLineOption).ExtraPromptLineCount
        $configuration = (New-TestPromptConfiguration @{
            Left = @()
            Right = @()
            Transient = $true
            ShellIntegration = 'None'
        })
    }

    AfterEach {
        Disable-Prompt
        (Get-PSReadLineOption).PromptText = $originalPromptText
        Set-PSReadLineOption -EditMode $(if ($originalMode -eq 'Windows') { 'Emacs' } else { 'Windows' })
        Set-PSReadLineOption -EditMode $originalMode
        Set-PSReadLineOption -ExtraPromptLineCount $originalExtraLines
        $function:global:prompt = $originalPrompt
        $function:global:PSConsoleHostReadLine = $originalReader
    }

    It 'clears a previous prompt suffix and restores it on disable' {
        $options = Get-PSReadLineOption
        $options.PromptText = [string[]]@('previous> ', 'invalid> ')
        $previousText = $options.PromptText
        Enable-Prompt -Configuration $configuration
        $options.PromptText.Count | Should -Be 1
        $options.PromptText[0] | Should -BeExactly ''
        Disable-Prompt
        [object]::ReferenceEquals($options.PromptText, $previousText) | Should -BeTrue
    }

    It 'preserves a suffix changed by another tool while enabled' {
        $options = Get-PSReadLineOption
        $options.PromptText = [string[]]@('previous> ')
        Enable-Prompt -Configuration $configuration
        $options.PromptText = [string[]]@('replacement> ')
        $replacementText = $options.PromptText
        Disable-Prompt
        [object]::ReferenceEquals($options.PromptText, $replacementText) | Should -BeTrue
    }
    It 'keeps acceptance bindings unchanged while reading and calls the reader once' -ForEach @('Windows', 'Vi') {
        Set-PSReadLineOption -EditMode $_
        $probe = [pscustomobject]@{ Calls = 0; Handlers = @() }
        $function:global:PSConsoleHostReadLine = {
            $probe.Calls++
            $probe.Handlers = @(Get-PSReadLineKeyHandler -Bound | Where-Object Function -eq 'StatusLineAcceptLine')
            'reader result'
        }.GetNewClosure()
        $before = @(Get-PSReadLineKeyHandler -Bound | Where-Object Function -eq 'AcceptLine')
        Enable-Prompt -Configuration $configuration

        (Get-PSReadLineKeyHandler -Bound | Where-Object Function -eq 'StatusLineAcceptLine') | Should -BeNullOrEmpty
        $result = PSConsoleHostReadLine

        $result | Should -BeExactly 'reader result'
        $probe.Calls | Should -Be 1
        $probe.Handlers.Count | Should -Be 0
        (Get-PSReadLineKeyHandler -Bound | Where-Object Function -eq 'StatusLineAcceptLine') | Should -BeNullOrEmpty
        @(Get-PSReadLineKeyHandler -Bound | Where-Object Function -eq 'AcceptLine').Count | Should -Be $before.Count
    }

    It 'preserves acceptance bindings when the reader throws' {
        Set-PSReadLineOption -EditMode Windows
        $function:global:PSConsoleHostReadLine = { throw 'reader failed' }
        Enable-Prompt -Configuration $configuration
        { PSConsoleHostReadLine } | Should -Throw '*reader failed*'
        (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -BeExactly 'AcceptLine'
    }

    It 'leaves the user extra line count unchanged with a two-row prompt' {
        Set-PSReadLineOption -ExtraPromptLineCount 4
        $configuration = (New-TestPromptConfiguration @{
            Right = @('Duration')
            Rows = 1
            Transient = $false
            ShellIntegration = 'None'
        })
        Enable-Prompt -Configuration $configuration
        (Get-PSReadLineOption).ExtraPromptLineCount | Should -Be 4
        $null = & prompt
        (Get-PSReadLineOption).ExtraPromptLineCount | Should -Be 4
        Disable-Prompt
        (Get-PSReadLineOption).ExtraPromptLineCount | Should -Be 4
    }

    It 'preserves custom acceptance and validation handlers during reading' {
        Set-PSReadLineOption -EditMode Windows
        Set-PSReadLineKeyHandler -Chord Enter -BriefDescription CustomEnter -ScriptBlock { }
        Set-PSReadLineKeyHandler -Chord Ctrl+Enter -Function ValidateAndAcceptLine
        $probe = [pscustomobject]@{ Enter = ''; Validate = '' }
        $function:global:PSConsoleHostReadLine = {
            $probe.Enter = (Get-PSReadLineKeyHandler -Chord Enter).Function
            $probe.Validate = (Get-PSReadLineKeyHandler -Chord Ctrl+Enter).Function
            'command'
        }.GetNewClosure()
        Enable-Prompt -Configuration $configuration
        PSConsoleHostReadLine | Should -BeExactly 'command'
        $probe.Enter | Should -BeExactly 'CustomEnter'
        $probe.Validate | Should -BeExactly 'ValidateAndAcceptLine'
        (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -BeExactly 'CustomEnter'
    }

    It 'does not restore over a binding replaced during reading' {
        Set-PSReadLineOption -EditMode Windows
        $function:global:PSConsoleHostReadLine = {
            Set-PSReadLineKeyHandler -Chord Enter -BriefDescription ReplacementEnter -ScriptBlock { }
            $null
        }
        Enable-Prompt -Configuration $configuration
        $null = PSConsoleHostReadLine
        (Get-PSReadLineKeyHandler -Chord Enter).Function | Should -BeExactly 'ReplacementEnter'
    }
}
