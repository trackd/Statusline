BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Optional prompt row connectors' -Tag Rendering {
    It 'renders multiline connectors only when explicitly enabled: <Connected>' -ForEach @(
        @{ Connected = $true; Expected = "╭─ LEFT`n╰─ > " }
        @{ Connected = $false; Expected = "LEFT`n> " }
    ) {
        $configuration = New-TestPromptConfiguration @{
            ConnectRows = $Connected; Rows = 2; Style = 'Plain'; Padding = ''
            ContextSymbol = '╭─ '; InputLinePrefix = '╰─ '
            Symbol = '> '; Left = @(); Right = @(); Input = @()
        }
        $text = Get-TestPrompt -Configuration $configuration -Block @(New-TestBlock 'LEFT') -Width 30
        ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly $Expected
    }

    It 'lets the switch override configured connectors in either direction' {
        $originalPrompt = $function:global:prompt
        try {
            foreach ($connected in $false, $true) {
                $configuration = New-TestPromptConfiguration @{
                    ConnectRows = (-not $connected); Rows = 2; Style = 'Plain'; Padding = ''
                    ContextSymbol = '╭─ '; InputLinePrefix = '╰─ '; Symbol = '> '
                    Left = @(); Right = @(); Input = @()
                    ShellIntegration = 'None'; Transient = $false
                }
                Enable-Prompt -Configuration $configuration -ConnectRows:$connected
                $text = (& prompt) -replace '\x1b\[[0-9;]*m'
                $text.Contains('╭─ ') | Should -Be $connected
                $text.Contains('╰─ ') | Should -Be $connected
                Disable-Prompt
            }
        }
        finally {
            Disable-Prompt
            $function:global:prompt = $originalPrompt
        }
    }
}
