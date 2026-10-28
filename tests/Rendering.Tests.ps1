BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Statusline rendering' -Tag Rendering {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'preserves styles and returns to the main display while sanitizing other controls' {
        $sequence = Write-TestStatusLine "$escape[34mhello$escape[0m`n$escape[2J" -AsEscapeSequence
        $sequence.Contains("$escape[34mhello$escape[0m") | Should -BeTrue
        $sequence.Contains("$escape[2J") | Should -BeFalse
        $sequence.StartsWith("$escape[2`$~$escape[1`$}", [StringComparison]::Ordinal) | Should -BeTrue
        $sequence.EndsWith("$escape[0`$}", [StringComparison]::Ordinal) | Should -BeTrue
        $sequence | Should -Not -Match '\x1b[78]'
        $sequence | Should -Not -Match '\x1b\[[0-9]+;[0-9]+H'
        $sequence.Contains("$escape[1`$}$escape[0m`r$escape[2K") | Should -BeTrue
    }

    It 'neutralizes colon-form SGR when the block is created' {
        $block = [StatusLine.BlockSnapshots]::Create("$escape[38:2::1:2:3mhi$escape[38;2;1;2;3mok", $null, $null)
        $block.Content.Contains("$escape[38:2") | Should -BeFalse
        $block.Content.Contains("$escape[38;2;1;2;3mok") | Should -BeTrue
    }

    It 'clips multi-chunk groups with Unicode enabled: <UseUnicode>' -ForEach @(
        @{ UseUnicode = $false }; @{ UseUnicode = $true }
    ) {
        $previousMeasurement = [StatusLine.TextMeasurement]::UseUnicode
        try {
            [StatusLine.TextMeasurement]::UseUnicode = $UseUnicode
            $env:TERM_PROGRAM = 'contour'
            $blocks = @(
                New-TestBlock ('x' * 12000) -Foreground '#123456' -Alignment Left -Padding ''
                New-TestBlock '日' -Foreground '#123456' -Alignment Center -Padding ''
                New-TestBlock 'R' -Foreground '#123456' -Alignment Right -Padding ''
            )
            foreach ($iteration in 1..3) {
                $sequence = Write-TestStatusLine -Block $blocks -Width 30 -BlockSeparator:$false -Style Plain -AsEscapeSequence
                $plain = ($sequence -replace '\x1b\[[0-?]*[ -/]*[@-~]') -replace '\r'
                $plain | Should -BeExactly (('x' * 13) + ' 日' + (' ' * 13) + 'R')
            }
        }
        finally {
            [StatusLine.TextMeasurement]::UseUnicode = $previousMeasurement
        }
    }
    It 'draws the icon before the text' {
        $block = New-TestBlock 'main' -Icon ([string][char]0xE0A0) -Padding ' '
        $block.Content | Should -BeExactly "$([char]0xE0A0) main"
        (Write-TestStatusLine -Block $block -Width 30 -AsEscapeSequence).Contains($block.Content) | Should -BeTrue
    }


    It 'wraps diamond segments in rounded caps' {
        $sequence = Write-TestStatusLine -Block (New-TestBlock 'x' -Background Navy) -Width 20 -Style Diamond -AsEscapeSequence
        $sequence.Contains([string][char]0xE0B6) | Should -BeTrue
        $sequence.Contains([string][char]0xE0B4) | Should -BeTrue
    }

    It 'positions pipeline blocks in all three groups' {
        $blocks = @(
            New-TestBlock 'LEFT'
            New-TestBlock 'CENTER' -Alignment Center
            New-TestBlock 'RIGHT' -Alignment Right
        )
        $sequence = $blocks | Write-TestStatusLine -Width 30 -AsEscapeSequence
        $sequence.Contains("$escape[1;13H") | Should -BeTrue
        $sequence.Contains("$escape[1;26H") | Should -BeTrue
    }

    It 'keeps the center fixed as the right group grows' {
        foreach ($rightText in @('R', 'DYNAMIC-BANDWIDTH')) {
            $blocks = @(
                New-TestBlock 'L'
                New-TestBlock 'CENTER' -Alignment Center
                New-TestBlock $rightText -Alignment Right
            )
            $sequence = Write-TestStatusLine -Block $blocks -Width 20 -AsEscapeSequence
            $sequence.Contains("$escape[1;8H") | Should -BeTrue
            $sequence.Contains('CENTER') | Should -BeTrue
        }
    }

    It 'places center text at the true center of a 131-column Contour frame' {
        $env:TERM_PROGRAM = 'contour'
        foreach ($rightText in @('R', ('R' * 40), ('R' * 100))) {
            $blocks = @(New-TestBlock 'L'; New-TestBlock 'CENTER' -Alignment Center; New-TestBlock $rightText -Alignment Right)
            $sequence = Write-TestStatusLine -Block $blocks -Width 131 -AsEscapeSequence
            $body = $sequence.Substring($sequence.IndexOf("$escape[2K", [StringComparison]::Ordinal) + 4)
            $body = $body.Substring(0, $body.IndexOf("$escape[0`$}", [StringComparison]::Ordinal)) -replace '\x1b\[[0-9;]*m'
            $body.Length | Should -Be 131
            $body.IndexOf('CENTER') | Should -Be 62
            $body[68] | Should -Be ' '
            $body | Should -Not -Match '\x1b\['
        }
    }

    It 'neutralizes Contour reverse-video in plain-style gaps' {
        $env:TERM_PROGRAM = 'contour'
        $blocks = @(New-TestBlock 'L' -Foreground '#123456'; New-TestBlock 'C' -Foreground '#123456' -Alignment Center)
        $sequence = Write-TestStatusLine -Block $blocks -Width 30 -Style Plain -AsEscapeSequence
        $sequence | Should -Match '\x1b\[0;7m +\x1b\[0m'
        $sequence.Contains('38;2;18;52;86;49mL') | Should -BeTrue
    }



    It 'clips blocks on narrow lines' {
        $blocks = @(New-TestBlock 'LEFT'; New-TestBlock 'RIGHT' -Alignment Right)
        $sequence = Write-TestStatusLine -Block $blocks -Width 6 -AsEscapeSequence
        $sequence.Contains('LEFT') | Should -BeTrue
        $sequence.Contains('RIGHT') | Should -BeFalse
    }

    It 'counts wide characters as two cells when clipping' {
        $sequence = Write-TestStatusLine -Block (New-TestBlock '日本') -Width 3 -AsEscapeSequence
        $sequence.Contains('日') | Should -BeTrue
        $sequence.Contains('本') | Should -BeFalse
    }

    It 'rejects objects that are not status line blocks' {
        $fake = [PSCustomObject]@{ Text = 'x'; Style = "$escape[2J"; Alignment = 'Left'; Padding = '' }
        { Write-TestStatusLine -Block $fake -Width 30 -AsEscapeSequence } | Should -Throw
    }

    It 'keeps plain block text free of added outer spacing' {
        $blocks = @(New-TestBlock 'one'; New-TestBlock 'two')
        $sequence = Write-TestStatusLine -Block $blocks -Width 30 -BlockSeparator:$false -AsEscapeSequence
        ($sequence -replace '\x1b\[[0-9;]*m', '').Contains('onetwo') | Should -BeTrue
    }

    It 'clears with empty text or blocks' {
        (Write-TestStatusLine '' -AsEscapeSequence).Contains("$escape[2K") | Should -BeTrue
        (Write-TestStatusLine -Block @() -Width 30 -AsEscapeSequence).Contains("$escape[2K") | Should -BeTrue
    }
}

Describe 'Block gradients' -Tag Rendering {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'steps gradient colors through the actual produced block count' {
        $blocks = @(New-TestBlock -Kind Status, User, Host, Os -StartColor Black -EndColor White)
        $blocks.Count | Should -Be 3
        $blocks[0].Foreground.Rgb.R | Should -Be 0
        $blocks[1].Foreground.Rgb.R | Should -Be 128
        $blocks[2].Foreground.Rgb.R | Should -Be 255
    }

    It 'uses the start color for a single gradient block' {
        (New-TestBlock -Kind User -StartColor '#123456' -EndColor White).Foreground.Rgb.ToArgb() | Should -Be ([Drawing.Color]::FromArgb(0x12,0x34,0x56).ToArgb())
    }

    It 'handles zero produced gradient blocks' {
        @(New-TestBlock -Kind Status -StartColor Black -EndColor White).Count | Should -Be 0
    }

    It 'fills powerline segments with gradient endpoints' {
        $blocks = @(New-TestBlock 'one'; New-TestBlock 'two')
        $text = Write-TestStatusLine -Block $blocks -Style Powerline -StartColor Black -EndColor White -AsEscapeSequence
        $text | Should -Match '48;2;0;0;0m'
        $text | Should -Match '48;2;255;255;255m'
    }
}

Describe 'VT parser and Unicode width regressions' -Tag Rendering {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'consumes complete character-set designations' {
        $sequence = New-TestBlock "a`e(0b`e(Bc" | Write-TestStatusLine -AsEscapeSequence
        $sequence | Should -Match 'a b c'
        $sequence | Should -Not -Match '\(0|\(B'
    }

    It 'consumes DCS through ST even when its payload includes BEL' {
        $sequence = New-TestBlock "a`ePdata`apayload`e\b" | Write-TestStatusLine -AsEscapeSequence
        $sequence | Should -Match 'a b'
        $sequence | Should -Not -Match 'payload|data'
    }

    It 'normalizes C1 SGR and consumes C1 OSC' {
        $sequence = New-TestBlock "a$([char]0x9b)31mb$([char]0x9d)2;title$([char]0x9c)c" | Write-TestStatusLine -AsEscapeSequence
        $sequence | Should -Match 'a\x1b\[31mb c'
        $sequence | Should -Not -Match 'title'
    }

    It 'does not render a wide angle bracket into a one-cell budget' {
        $sequence = New-TestBlock "$([char]0x2329)x" | Write-TestStatusLine -Width 1 -AsEscapeSequence
        $sequence | Should -Not -Match ([regex]::Escape([string][char]0x2329))
    }
}
