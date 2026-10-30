BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Rounded and Diamond shapes' -Tag Rendering {
    It 'renders Rounded in <Alignment> with Blend=<Blend>' -ForEach @(
        foreach ($blend in $false, $true) {
            @{ Alignment = 'Left'; Blend = $blend; Edge = 0xE0B4 }
            @{ Alignment = 'Center'; Blend = $blend; Edge = 0xE0B4 }
            @{ Alignment = 'Right'; Blend = $blend; Edge = 0xE0B6 }
        }
    ) {
        $configuration = [StatusLine.StatusBarConfiguration]@{
            Block = @([StatusLine.BlockSnapshots]::Create('日本', '#123456', '#456789', $Alignment, ''))
            Style = 'Rounded'; BlendBackground = $Blend; Padding = ''
        }
        $sequence = Show-StatusLine -Configuration $configuration -Width 16
        $visible = $sequence -replace '\x1b\[[0-9;]*m'
        $visible | Should -Match '日本'
        # Background blending draws edges as ramp-colored cells; the glyphs would be invisible.
        $sequence.Contains([char]$Edge) | Should -Be (-not $Blend)
        (Measure-Codepoint $visible).Columns | Should -BeLessOrEqual 16
        foreach ($width in @(1, 3, 7)) {
            $narrow = (Show-StatusLine -Configuration $configuration -Width $width) -replace '\x1b\[[0-9;]*m'
            (Measure-Codepoint $narrow).Columns | Should -BeLessOrEqual $width
        }
    }

    It 'joins Rounded blocks with a rounded separator and ignores BlockSeparator' {
        $configuration = [StatusLine.StatusBarConfiguration]@{
            Block = @([StatusLine.BlockSnapshots]::Create('A', '#123456', '#456789'), [StatusLine.BlockSnapshots]::Create('B', '#abcdef', '#fedcba'))
            Style = 'Rounded'; BlockSeparator = $true; Padding = ''
        }
        $visible = (Show-StatusLine -Configuration $configuration -Width 20) -replace '\x1b\[[0-9;]*m'
        $visible.TrimEnd() | Should -BeExactly "A$([char]0xE0B4)B$([char]0xE0B4)"
    }

    It 'draws each Diamond block as a separate pill with BlockSeparator=<Separator>' -ForEach @(
        @{ Separator = $false }; @{ Separator = $true }
    ) {
        $configuration = [StatusLine.StatusBarConfiguration]@{
            Block = @([StatusLine.BlockSnapshots]::Create('A', '#123456', '#456789'), [StatusLine.BlockSnapshots]::Create('B', '#abcdef', '#fedcba'))
            Style = 'Diamond'; BlockSeparator = $Separator; Padding = ''
        }
        $visible = (Show-StatusLine -Configuration $configuration -Width 20) -replace '\x1b\[[0-9;]*m'
        $pill = { param($text) "$([char]0xE0B6)$text$([char]0xE0B4)" }
        $visible.TrimEnd() | Should -BeExactly ((& $pill 'A') + ' ' + (& $pill 'B'))
    }
}
