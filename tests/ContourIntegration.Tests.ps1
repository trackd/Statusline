BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $contextType = [StatusLine.PromptSession].Assembly.GetType('StatusLine.ContourContext')
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
}

Describe 'Contour metadata lifecycle' -Tag Lifecycle {
    BeforeEach { $savedEnvironment = Enter-TestTerminalEnvironment; $env:TERM_PROGRAM = 'contour'; $env:WT_SESSION = $null }
    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedEnvironment }

    It 'disables reflow for a Contour prompt' {
        $configuration = New-TestPromptConfiguration @{ Left = @(); Right = @(); Symbol = '> '; ShellIntegration = 'Osc133' }
        Get-TestPrompt -Configuration $configuration -IncludeIntegration | Should -Match '\x1b\[\?2028l'
    }

    It 'pairs contexts and escapes command metadata without inserting semantic marks' {
        $context = [Activator]::CreateInstance($contextType, $true)
        $promptMethod = $contextType.GetMethod('Prompt', $flags)
        $executeMethod = $contextType.GetMethod('Execute', $flags)
        $closeMethod = $contextType.GetMethod('Close', $flags)
        $first = $promptMethod.Invoke($context, @('C:\folder;name', $null))
        $first | Should -Match 'type=shell;hostname='
        $first | Should -Match 'cwd=C:\\x5cfolder\\x3bname'
        $command = $executeMethod.Invoke($context, @('echo a;b\c', 'C:\folder;name'))
        $command | Should -Match 'type=command'
        $command | Should -Match 'cmdline=echo a\\x3bb\\x5cc'
        $id = [regex]::Match($command, 'start=([^;]+)').Groups[1].Value
        $next = $promptMethod.Invoke($context, @('C:\other', [int]7))
        $next | Should -Match ([regex]::Escape("end=$id;exit=failure;status=7"))
        $next | Should -Not -Match '\x1b\]133;'
        $closeMethod.Invoke($context, @()) | Should -Match '\x1b\]3008;end='
        $closeMethod.Invoke($context, @()) | Should -BeExactly ''
    }
}