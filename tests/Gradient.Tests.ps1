BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"

    function Get-GradientFrame {
        param([string]$Sequence)
        $begin = $Sequence.IndexOf("$escape[2K", [StringComparison]::Ordinal) + 4
        $end = $Sequence.LastIndexOf("$escape[0`$}", [StringComparison]::Ordinal)
        $body = $Sequence.Substring($begin, $end - $begin)
        $background = $null
        $foreground = $null
        foreach ($token in [regex]::Matches($body, '\x1b\[(?<style>[0-9;]*)m|(?<text>[^\x1b]+)')) {
            if ($token.Groups['style'].Success) {
                $style = $token.Groups['style'].Value
                if ($style -match '^(0;|0$|$)') { $background = $null; $foreground = $null }
                $backgroundMatch = [regex]::Match($style, '(?:^|;)48;2;(\d+);(\d+);(\d+)')
                if ($backgroundMatch.Success) { $background = ($backgroundMatch.Groups[1..3].Value -join ',') }
                $foregroundMatch = [regex]::Match($style, '(?:^|;)38;2;(\d+);(\d+);(\d+)')
                if ($foregroundMatch.Success) { $foreground = ($foregroundMatch.Groups[1..3].Value -join ',') }
                if ($style -match '(^|;)31(;|$)') { $foreground = 'ansi31' }
            }
            else {
                foreach ($character in $token.Groups['text'].Value.ToCharArray()) {
                    [pscustomobject]@{ Text = [string]$character; Background = $background; Foreground = $foreground }
                }
            }
        }
    }
}

Describe 'Full-width statusline gradient' -Tag Rendering {
    It 'keeps numerical grouping <Number> in one contrast color across the ramp boundary' -ForEach @(
        @{ Number = '10' }; @{ Number = '12.34' }; @{ Number = '1,234.56' }
        @{ Number = '-10' }; @{ Number = '+10' }; @{ Number = '1.2e-10' }
        @{ Number = '10%' }; @{ Number = '١٠' }; @{ Number = '１０' }
        @{ Number = "1$([char]0x202f)234,5" }
    ) {
        $previousMeasurement = [StatusLine.TextMeasurement]::UseUnicode
        try {
            foreach ($unicode in $false, $true) {
                [StatusLine.TextMeasurement]::UseUnicode = $unicode
                $blocks = @(
                    (New-TestBlock (('A' * 13) + $Number + 'Z') -Background '#000000' -Padding '')
                    (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right)
                )
                $sequence = Write-TestStatusLine -Block $blocks -Width 30 -Style Powerline -BlendBackground -AsEscapeSequence
                $cells = @(Get-GradientFrame $sequence)
                $numericCells = $cells[13..(12 + $Number.Length)]
                ($numericCells.Text -join '') | Should -BeExactly $Number
                @($numericCells.Foreground | Select-Object -Unique).Count | Should -Be 1
                @($numericCells.Background | Select-Object -Unique).Count | Should -BeGreaterThan 1
            }
        }
        finally { [StatusLine.TextMeasurement]::UseUnicode = $previousMeasurement }
    }

    It 'allows separate numbers to choose different contrast colors' {
        $blocks = @(
            (New-TestBlock (('A' * 13) + '10/20') -Background '#000000' -Padding '')
            (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right)
        )
        $cells = @(Get-GradientFrame (Write-TestStatusLine -Block $blocks -Width 30 -Style Powerline -BlendBackground -AsEscapeSequence))
        $cells[13].Foreground | Should -BeExactly $cells[14].Foreground
        $cells[16].Foreground | Should -BeExactly $cells[17].Foreground
        $cells[13].Foreground | Should -Not -BeExactly $cells[16].Foreground
    }

    It 'retains caller-supplied numeric colors and explicit contrast modes' {
        $blocks = @(
            (New-TestBlock (('A' * 13) + "$escape[31m10$escape[39m 20") -Background '#000000' -Foreground '#11aa22' -Padding '')
            (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right)
        )
        foreach ($mode in 'Automatic', 'Preserve', 'Black', 'White') {
            $options = [StatusLine.BlockRenderOptions]@{ Width = 30; Style = 'Powerline'; BlendBackground = $true; TextContrast = $mode }
            $cells = @(Get-GradientFrame ([StatusLine.TerminalControl]::BlocksSequence($blocks, $options)))
            $cells[13].Foreground | Should -BeExactly 'ansi31'
            $cells[14].Foreground | Should -BeExactly 'ansi31'
            $cells[16].Foreground | Should -BeExactly $cells[17].Foreground
        }
    }

    It 'clips a numerical grouping without painting outside the available row' {
        $blocks = @(
            (New-TestBlock ('1234567890' * 4) -Background '#000000' -Padding '')
            (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right)
        )
        $cells = @(Get-GradientFrame (Write-TestStatusLine -Block $blocks -Width 12 -Style Powerline -BlendBackground -AsEscapeSequence))
        $cells.Count | Should -Be 12
        ($cells.Text -join '') | Should -BeExactly '123456789012'
        @($cells.Foreground | Select-Object -Unique).Count | Should -Be 1
    }

    It 'uses one ramp across text and all remaining columns for <Style>' -ForEach @(
        @{ Style = 'Powerline' }; @{ Style = 'Diamond' }
    ) {
        $blocks = @((New-TestBlock 'ABCDEFGHIJK' -Background '#000000' -Padding ''), (New-TestBlock 'END' -Background '#ffffff' -Padding ''))
        $sequence = Write-TestStatusLine -Block $blocks -Width 21 -Style $Style -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $cells = @(Get-GradientFrame $sequence)
        $cells.Count | Should -Be 21
        $cells[0].Background | Should -BeExactly '0,0,0'
        $cells[10].Background | Should -BeExactly '128,128,128'
        $cells[20].Background | Should -BeExactly '255,255,255'
        $word = @($cells | Where-Object { $_.Text -cmatch '^[A-K]$' } | Select-Object -First 11)
        @($word.Background | Select-Object -Unique).Count | Should -Be 11
        ($cells | Where-Object Text -EQ 'A').Foreground | Should -BeExactly '255,255,255'
        ($cells | Where-Object Text -EQ 'K').Foreground | Should -BeExactly '0,0,0'
        $cells[20].Foreground | Should -BeExactly '0,0,0'
        $sequence | Should -Not -Match '\x1b\[1;\d+H'
    }

    It 'keeps both the center and the ramp fixed as right-hand text changes' {
        foreach ($rightText in @('R', ('R' * 40))) {
            $blocks = @((New-TestBlock 'L' -Background '#0000ff' -Padding ''), (New-TestBlock 'CENTER' -Background '#00ff00' -Padding '' -Alignment Center), (New-TestBlock $rightText -Background '#ff0000' -Padding '' -Alignment Right))
            $sequence = Write-TestStatusLine -Block $blocks -Width 131 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
            $cells = @(Get-GradientFrame $sequence)
            $cells.Count | Should -Be 131
            ($cells.Text -join '').IndexOf('CENTER') | Should -Be 62
            $cells[65].Background | Should -BeExactly '128,0,128'
            $cells[130].Background | Should -BeExactly '255,0,0'
        }
    }

    It 'retains explicit warning foreground colors over the changing background' {
        $blocks = @((New-TestBlock "$escape[31m!$escape[39m" -Background '#000000' -Padding ''), (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right))
        $sequence = Write-TestStatusLine -Block $blocks -Width 20 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $cells = @(Get-GradientFrame $sequence)
        ($cells | Where-Object Text -EQ '!').Foreground | Should -BeExactly 'ansi31'
    }

    It 'preserves wide and combining graphemes while filling the row' {
        $combined = "e$([char]0x0301)"
        $blocks = @((New-TestBlock "日$combined" -Background '#000000' -Padding ''), (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right))
        $sequence = Write-TestStatusLine -Block $blocks -Width 20 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $begin = $sequence.IndexOf("$escape[2K", [StringComparison]::Ordinal) + 4
        $body = $sequence.Substring($begin, $sequence.LastIndexOf("$escape[0`$}", [StringComparison]::Ordinal) - $begin)
        [StatusLine.Unicode]::Measure($body).Columns | Should -Be 20
        $body.Contains($combined) | Should -BeTrue
    }

    It 'retains theme palette colors when their RGB values are unknown' {
        $block = New-TestBlock 'A' -Background ([ConsoleColor]::DarkBlue) -Foreground ([ConsoleColor]::Gray) -Padding ''
        $sequence = Write-TestStatusLine -Block $block -Width 10 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $sequence | Should -Match '\x1b\[0;37;44mA'
        $sequence | Should -Not -Match '48;2;'
    }

    It 'preserves configured foregrounds when contrast mode is Preserve' {
        $blocks = @((New-TestBlock 'A' -Background '#000000' -Foreground '#11aa22' -Padding ''), (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right))
        $options = [StatusLine.BlockRenderOptions]@{ Width = 20; Style = 'Powerline'; BlockSeparator = $false; BlendBackground = $true; TextContrast = 'Preserve' }
        $cells = @(Get-GradientFrame ([StatusLine.TerminalControl]::BlocksSequence($blocks, $options)))
        ($cells | Where-Object Text -EQ 'A').Foreground | Should -BeExactly '17,170,34'
    }

    It 'clips each group without exceeding the full row for <Style>' -ForEach @(
        @{ Style = 'Powerline' }; @{ Style = 'Diamond' }
    ) {
        $blocks = @((New-TestBlock 'LEFTLONG' -Background '#000000' -Padding ' '), (New-TestBlock 'CENTER' -Background '#888888' -Padding ' ' -Alignment Center), (New-TestBlock 'RIGHTLONG' -Background '#ffffff' -Padding ' ' -Alignment Right))
        foreach ($width in 1, 2, 3, 4, 5, 9, 20, 131) {
            $sequence = Write-TestStatusLine -Block $blocks -Width $width -Style $Style -BlockSeparator:$false -BlendBackground -AsEscapeSequence
            @(Get-GradientFrame $sequence).Count | Should -Be $width
        }
    }

    It 'preserves an explicitly white custom foreground rather than guessing it is generated contrast' {
        $blocks = @((New-TestBlock 'A' -Background '#000000' -Padding ''), (New-TestBlock "$escape[38;2;255;255;255m!$escape[39m" -Background '#ffffff' -Padding '' -Alignment Right))
        $sequence = Write-TestStatusLine -Block $blocks -Width 20 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $cells = @(Get-GradientFrame $sequence)
        ($cells | Where-Object Text -EQ '!').Foreground | Should -BeExactly '255,255,255'
    }

    It 'does not mistake a cap glyph inside user content for a generated edge' {
        $blocks = @((New-TestBlock '' -Background '#000000' -Padding ''), (New-TestBlock 'R' -Background '#ffffff' -Padding '' -Alignment Right))
        $sequence = Write-TestStatusLine -Block $blocks -Width 20 -Style Powerline -BlockSeparator:$false -BlendBackground -AsEscapeSequence
        $cells = @(Get-GradientFrame $sequence)
        $cells[0].Foreground | Should -BeExactly '255,255,255'
        $cells[0].Background | Should -BeExactly '0,0,0'
    }
}
