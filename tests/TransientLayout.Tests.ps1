BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $layoutType = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.TransientPromptLayout')
    $getRow = $layoutType.GetMethod('GetRow', [Reflection.BindingFlags]'Static,NonPublic')
}

Describe 'Accepted input transient layout' -Tag Rendering {
    It 'locates the previous prompt for <Label>' -ForEach @(
        @{ Label = 'empty input'; Text = ''; Width = 80; Column = 4; Context = 1; Expected = 8 }
        @{ Label = 'one-row prompts'; Text = 'abc'; Width = 80; Column = 4; Context = 0; Expected = 9 }
        @{ Label = 'two-row prompts'; Text = 'abc'; Width = 80; Column = 4; Context = 1; Expected = 8 }
        @{ Label = 'a wrapped command'; Text = 'abcdefgh'; Width = 8; Column = 4; Context = 1; Expected = 7 }
        @{ Label = 'an exact-width command'; Text = 'abcd'; Width = 8; Column = 4; Context = 1; Expected = 7 }
        @{ Label = 'multiline input'; Text = "a`nb"; Width = 80; Column = 4; Context = 1; Expected = 7 }
        @{ Label = 'a newline at the wrap boundary'; Text = "abcd`nx"; Width = 8; Column = 4; Context = 1; Expected = 7 }
    ) {
        $getRow.Invoke($null, @($Text, $Column, $Width, 10, $Context, '>> ')) | Should -Be $Expected
    }

    It 'keeps the original prompt when its origin has scrolled off screen' {
        $getRow.Invoke($null, @('long command', 4, 8, 1, 1, '>> ')) | Should -BeNullOrEmpty
    }

    It 'keeps the original prompt when editor character widths are uncertain' -ForEach @("a`t", "a$([char]0x0301)", '日本', '🙂') {
        $getRow.Invoke($null, @($_, 4, 80, 10, 1, '>> ')) | Should -BeNullOrEmpty
    }
}
