BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    if (-not ('UnicodeProbe' -as [type])) {
        $probeSource = "#nullable enable`nglobal using System;`nglobal using System.Collections.Generic;`nglobal using System.Runtime.CompilerServices;`nglobal using System.Runtime.InteropServices;`n"
        $sourcePaths = @(
            'Terminal/Vt.Controls.cs'
            'Rendering/TerminalSequence.cs'
            'Rendering/Unicode/Unicode.cs'
            'Rendering/Unicode/Unicode.Helpers.cs'
            'Rendering/Unicode/Unicode.Grapheme.cs'
            'Rendering/Unicode/Unicode.Generated.cs'
        )
        foreach ($name in $sourcePaths) {
            $part = [IO.File]::ReadAllText("$PSScriptRoot/../src/Core/$name")
            $probeSource += $part.Replace('namespace StatusLine;', 'namespace UnicodeUnderTest {') + "`n}`n"
        }
        $probeSource += [IO.File]::ReadAllText("$PSScriptRoot/UnicodeProbe.cs")
        $probeSource = [regex]::Replace($probeSource, '(?m)^using (?:System|System.Collections.Generic|System.Runtime.CompilerServices|System.Runtime.InteropServices);\r?\n', '')
        Add-Type -TypeDefinition $probeSource
    }
}

Describe 'Unicode measurement trial' -Tag Rendering {
    BeforeEach { $previousMeasurement = [StatusLine.TextMeasurement]::UseUnicode }
    AfterEach { [StatusLine.TextMeasurement]::UseUnicode = $previousMeasurement }

    It 'measures <Label> in terminal columns' -ForEach @(
        @{ Label = 'ASCII'; Text = 'abc'; Columns = 3; Graphemes = 3 }
        @{ Label = 'CJK'; Text = '日本'; Columns = 4; Graphemes = 2 }
        @{ Label = 'combining text'; Text = "ab$([char]0x0301)"; Columns = 2; Graphemes = 2 }
        @{ Label = 'emoji'; Text = '🧠'; Columns = 2; Graphemes = 1 }
        @{ Label = 'ZWJ family'; Text = '👨‍👩‍👧‍👦'; Columns = 2; Graphemes = 1 }
        @{ Label = 'flag'; Text = '🇸🇪'; Columns = 2; Graphemes = 1 }
        @{ Label = 'trailing SGR'; Text = "`e[31mabc`e[0m"; Columns = 3; Graphemes = 3 }
        @{ Label = 'VT within a grapheme'; Text = "a`e[31m$([char]0x0301)"; Columns = 1; Graphemes = 1 }
        @{ Label = 'OSC'; Text = "`e]2;title`aX"; Columns = 1; Graphemes = 1 }
        @{ Label = 'incomplete CSI'; Text = "abc`e[31"; Columns = 3; Graphemes = 3 }
        @{ Label = 'empty'; Text = ''; Columns = 0; Graphemes = 0 }
    ) {
        $result = [StatusLine.Unicode]::Measure($Text)
        $result.Columns | Should -Be $Columns
        $result.CursorMovements | Should -Be $Graphemes
        $result.Offset | Should -Be $Text.Length
        [StatusLine.Unicode]::UnicodeLength($Text) | Should -Be $Columns
    }

    It 'keeps whole graphemes when clipping and preserves the legacy implementation' {
        foreach ($unicodeMode in @($false, $true)) {
            [StatusLine.TextMeasurement]::UseUnicode = $unicodeMode
            $configuration = New-TestPromptConfiguration @{
                Left = @(); Right = @(); Input = @()
                ContextSymbol = ''; InputLinePrefix = ''; Symbol = '> '; TransientSymbol = '> '
                Rows = 2; Padding = ''; Style = 'Plain'; }
            $text = Get-TestPrompt -Configuration $configuration -Block @(New-TestBlock '日本') -Width 4
            ($text -replace '\x1b\[[0-9;]*m') | Should -BeExactly "日`n> "
        }
    }

    It 'returns offsets, grapheme counts, columns and VT detection' {
        $text = "`e[31m日本`e[0m"
        $measurement = [StatusLine.Unicode]::Measure($text)
        $measurement.GetType().Name | Should -BeExactly 'MeasurementResult'
        $measurement.CursorMovements | Should -Be 2
        $measurement.Columns | Should -Be 4
        $measurement.Offset | Should -Be $text.Length
        $measurement.ContainsVT | Should -BeTrue
        [StatusLine.Unicode]::Measure('abc').ContainsVT | Should -BeFalse
        $plain = [StatusLine.Unicode]::Measure('日本', 0, 1, 2, $false)
        $plain.CursorMovements | Should -Be 1
        $plain.Columns | Should -Be 2
        $plain.Offset | Should -Be 1
        $plain.ContainsVT | Should -BeFalse
    }
    It 'resolves ambiguous private-use Powerline glyphs as narrow' {
        foreach ($glyph in @([string][char]0xe0b0, [string][char]0xe0b2)) {
            [StatusLine.Unicode]::Measure($glyph).Columns | Should -Be 1
            [StatusLine.TextMeasurement]::MeasureLegacy($glyph) | Should -Be 1
        }
    }
    It 'uses one width policy in VT and plain measurement' {
        foreach ($useVT in @($false, $true)) {
            $powerline = [string][char]0xe0b0
            [StatusLine.Unicode]::Measure($powerline, 0, [int]::MaxValue, [int]::MaxValue, $useVT).Columns | Should -Be 1
            $emoji = [StatusLine.Unicode]::Measure('❤️', 0, [int]::MaxValue, [int]::MaxValue, $useVT)
            $emoji.Columns | Should -Be 2
            $emoji.CursorMovements | Should -Be 1
            $emoji.Offset | Should -Be 2
        }
        [StatusLine.Unicode]::Measure('❤️').Columns | Should -Be 2
    }

    It 'honors grapheme budgets without splitting clusters across styling' {
        $bounded = [StatusLine.Unicode]::Measure('❤️日本', 0, 1, 2, $false)
        $bounded.CursorMovements | Should -Be 1
        $bounded.Columns | Should -Be 2
        $bounded.Offset | Should -Be 2
        $text = "a`e[31m$([char]0x0301)"
        $measurement = [StatusLine.Unicode]::Measure($text)
        $measurement.CursorMovements | Should -Be 1
        $measurement.Columns | Should -Be 1
        $measurement.Offset | Should -Be $text.Length
        $measurement.ContainsVT | Should -BeTrue
        [StatusLine.Unicode]::Measure($text, 0, 1, 1).Offset | Should -Be $text.Length
    }
    It 'matches the pinned Windows Terminal iterator state transitions' {
        [UnicodeProbe]::CompareIterator() | Should -BeGreaterThan 1000
    }


    It 'separates plain measurement from VT measurement' {
        [StatusLine.Unicode]::Measure('日本').Columns | Should -Be 4
        [StatusLine.Unicode]::Measure('日本', 0, 1, 2).Offset | Should -Be 1
        { [StatusLine.Unicode]::Measure('abc', -1) } | Should -Throw
    }
}

