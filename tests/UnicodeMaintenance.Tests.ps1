Describe 'Offline width-table maintenance' -Tag 'Maintenance' {
    BeforeEach {
        $inputDirectory = Join-Path $TestDrive 'unicode-input'
        $null = New-Item -ItemType Directory -Path $inputDirectory -Force
        $candidate = Join-Path $TestDrive 'candidate.cs'
    }

    It 'merges sorted width ranges and writes a candidate without changing production tables' {
        @('3002..3003 ; W', '3000..3001 ; F', '0041 ; Na') |
            Set-Content -LiteralPath (Join-Path $inputDirectory 'EastAsianWidth.txt')
        '1F9E0 ; Emoji_Presentation' | Set-Content -LiteralPath (Join-Path $inputDirectory 'emoji-data.txt')
        $production = Join-Path $PSScriptRoot '../src/Core/Rendering/Unicode/CellWidthData.cs'
        $before = (Get-FileHash -LiteralPath $production).Hash
        & "$PSScriptRoot/../tools/Update-CellWidthData.ps1" -InputDirectory $inputDirectory -OutputPath $candidate
        $text = Get-Content -LiteralPath $candidate -Raw
        $text | Should -Match '0x3000, 0x3003'
        $text | Should -Match '0x1F9E0, 0x1F9E0'
        (Get-FileHash -LiteralPath $production).Hash | Should -Be $before
        @(Get-ChildItem $TestDrive -Filter '*.tmp').Count | Should -Be 0
    }

    It 'rejects <Label> before replacing an existing candidate' -ForEach @(
        @{ Label = 'empty width data'; Wide = '# no ranges'; Emoji = '1F9E0 ; Emoji_Presentation' }
        @{ Label = 'reversed ranges'; Wide = '3003..3000 ; W'; Emoji = '1F9E0 ; Emoji_Presentation' }
        @{ Label = 'invalid codepoints'; Wide = '110000 ; W'; Emoji = '1F9E0 ; Emoji_Presentation' }
        @{ Label = 'empty emoji data'; Wide = '3000 ; W'; Emoji = '# no emoji' }
    ) {
        $Wide | Set-Content -LiteralPath (Join-Path $inputDirectory 'EastAsianWidth.txt')
        $Emoji | Set-Content -LiteralPath (Join-Path $inputDirectory 'emoji-data.txt')
        'retained candidate' | Set-Content -LiteralPath $candidate
        { & "$PSScriptRoot/../tools/Update-CellWidthData.ps1" -InputDirectory $inputDirectory -OutputPath $candidate } | Should -Throw '*Unicode input*'
        (Get-Content -LiteralPath $candidate -Raw).Trim() | Should -BeExactly 'retained candidate'
    }
}
