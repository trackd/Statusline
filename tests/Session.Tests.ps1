BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Session counters' -Tag Lifecycle {
    It 'captures retained errors without changing the error collection' {
        $retainedError = [Management.Automation.ErrorRecord]::new([Exception]::new('counter test'), 'CounterTest', 'NotSpecified', $null)
        $global:Error.Insert(0, $retainedError)
        try {
            $expectedCount = $global:Error.Count
            (New-TestBlock -Kind Errors -IconSet None).Text | Should -BeExactly "$expectedCount"
            $global:Error.Count | Should -Be $expectedCount
        }
        finally { $global:Error.Remove($retainedError) }
    }

    It 'counts the default location stack' {
        Push-Location -LiteralPath $TestDrive
        try {
            $expectedCount = @(Get-Location -Stack).Count
            (New-TestBlock -Kind StackCount).Text | Should -BeExactly "$expectedCount"
        }
        finally { Pop-Location }
    }

    It 'matches local runspace enumeration' {
        $expectedCount = @(Get-Runspace).Count
        (New-TestBlock -Kind Runspaces).Text | Should -BeExactly "$expectedCount"
    }

    It 'omits nested prompt depth at the top level' {
        @(New-TestBlock -Kind NestedPrompt).Count | Should -Be 0
    }
}

Describe 'Terminal process ancestry' -Tag Lifecycle {
    BeforeAll {
        $detector = [StatusLine.TerminalControl].Assembly.GetType('StatusLine.TerminalProcessDetector')
        $findAncestor = $detector.GetMethod('FindAncestor', [Reflection.BindingFlags]'Static,NonPublic')
    }

    It 'walks through conhost to the terminal' {
        $processTree = [Collections.Generic.Dictionary[int, ValueTuple[int,string]]]::new()
        $processTree.Add(1, [ValueTuple[int,string]]::new(2, 'pwsh'))
        $processTree.Add(2, [ValueTuple[int,string]]::new(3, 'conhost'))
        $processTree.Add(3, [ValueTuple[int,string]]::new(0, 'WindowsTerminal'))
        $findAncestor.Invoke($null, @([int]1, $processTree)) | Should -Be 3
    }

    It 'terminates a cyclic ancestry chain' {
        $processTree = [Collections.Generic.Dictionary[int, ValueTuple[int,string]]]::new()
        $processTree.Add(1, [ValueTuple[int,string]]::new(2, 'pwsh'))
        $processTree.Add(2, [ValueTuple[int,string]]::new(1, 'launcher'))
        $findAncestor.Invoke($null, @([int]1, $processTree)) | Should -Be 0
    }

    It 'limits ancestry traversal to 32 processes' {
        $processTree = [Collections.Generic.Dictionary[int, ValueTuple[int,string]]]::new()
        foreach ($processNumber in 1..33) {
            $processTree.Add($processNumber, [ValueTuple[int,string]]::new(($processNumber + 1), 'launcher'))
        }
        $processTree.Add(34, [ValueTuple[int,string]]::new(0, 'WindowsTerminal'))
        $findAncestor.Invoke($null, @([int]1, $processTree)) | Should -Be 0
    }

    It 'caches native process detection' {
        $firstResult = [StatusLine.TerminalControl]::DetectProcess()
        [StatusLine.TerminalControl]::DetectProcess() | Should -Be $firstResult
    }
}

Describe 'Invocation history block' -Tag Lifecycle {
    It 'renders the captured invocation ID with the original icon and a normal space' {
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @('HistoryId'); IconSet = 'NerdFont'; Padding = ' '
            State = [StatusLine.StatusLineState]@{ HistoryId = 42 }
        }
        $block = [StatusLine.BlockSnapshots]::Snapshot($options)[0]
        $block.Text | Should -BeExactly '42'
        $block.Icon | Should -BeExactly ([char]::ConvertFromUtf32(0xf060d))
        $render = [StatusLine.BlockRenderOptions]@{ Width = 30; Style = 'Plain'; BlockSeparator = $false; TextContrast = 'Preserve' }
        $text = [StatusLine.TerminalControl]::BlocksSequence(@($block), $render) -replace '\x1b\[[0-9;]*m'
        $text | Should -Match ([regex]::Escape("$([char]::ConvertFromUtf32(0xf060d)) 42"))
        $text | Should -Not -Match ([string][char]0x2009)
    }

    It 'omits history ID when no invocation was captured' {
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @('HistoryId'); IconSet = 'None'; Padding = ''
            State = [StatusLine.StatusLineState]::new()
        }
        @([StatusLine.BlockSnapshots]::Snapshot($options)).Count | Should -Be 0
    }
}