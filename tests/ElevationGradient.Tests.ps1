BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    $layoutType = [StatusLine.PromptConfiguration].Assembly.GetType('StatusLine.PromptLayout')
    $layoutConstructor = $layoutType.GetConstructor([Reflection.BindingFlags]'Instance,NonPublic', $null, [type[]]@([StatusLine.PromptConfiguration], [bool]), $null)
}

Describe 'Prompt elevation gradient' -Tag Rendering {
    It 'selects the expected endpoints when elevated is <Elevated>' -ForEach @(
        @{ Elevated = $false; ExpectedStart = '#123456'; ExpectedEnd = '#abcdef' }
        @{ Elevated = $true; ExpectedStart = '#ff7b00'; ExpectedEnd = '#cc2200' }
    ) {
        $configuration = New-TestPromptConfiguration @{
            Left = @('Directory'); StartColor = '#123456'; EndColor = '#abcdef'
            ElevatedColorStart = '#ff7b00ff'; ElevatedColorEnd = [Drawing.Color]::FromArgb(204, 34, 0)
        }
        $layout = $layoutConstructor.Invoke(@($configuration, $Elevated))
        $layout.StartColor.ToArgb() | Should -Be ([Drawing.ColorTranslator]::FromHtml($ExpectedStart).ToArgb())
        $layout.EndColor.ToArgb() | Should -Be ([Drawing.ColorTranslator]::FromHtml($ExpectedEnd).ToArgb())
        $accent = $layoutType.GetProperty('TransientAccent', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($layout)
        $accent.Rgb.ToArgb() | Should -Be $layout.StartColor.ToArgb()
    }

    It 'retains normal colors without an elevation override' {
        $configuration = New-TestPromptConfiguration @{ StartColor = 'Blue'; EndColor = 'Green'; ElevatedColorStart = $null; ElevatedColorEnd = $null }
        $layout = $layoutConstructor.Invoke(@($configuration, $true))
        $layout.StartColor.ToArgb() | Should -Be ([Drawing.Color]::Blue.ToArgb())
        $layout.EndColor.ToArgb() | Should -Be ([Drawing.Color]::Green.ToArgb())
    }

    It 'supports an elevation gradient without normal endpoints' {
        $configuration = New-TestPromptConfiguration @{ StartColor = $null; EndColor = $null; ElevatedColorStart = 'Orange'; ElevatedColorEnd = 'Red' }
        $layout = $layoutConstructor.Invoke(@($configuration, $true))
        $layout.StartColor.ToArgb() | Should -Be ([Drawing.Color]::Orange.ToArgb())
        $layout.EndColor.ToArgb() | Should -Be ([Drawing.Color]::Red.ToArgb())
    }

    It 'validates elevation colors even in an unelevated session' -ForEach @(
        @{ Start = 'Red'; End = $null }
        @{ Start = $null; End = 'Red' }
        @{ Start = 'Red'; End = 'UnknownColor' }
        @{ Start = 'UnknownColor'; End = 'Red' }
        @{ Start = [Drawing.Color]::Empty; End = 'Red' }
    ) {
        $configuration = New-TestPromptConfiguration @{ ElevatedColorStart = $Start; ElevatedColorEnd = $End }
        { $layoutConstructor.Invoke(@($configuration, $false)) } | Should -Throw
    }

    It 'binds cmdlet endpoints and permits clearing them through runtime configuration' {
        try {
            $configuration = New-TestPromptConfiguration @{ Left = @(); Right = @(); ShellIntegration = 'None'; Transient = $false }
            Enable-Prompt -Configuration $configuration -ElevatedColorStart '#ff7b00ff' -ElevatedColorEnd Red
            [StatusLine.Prompt]::Configuration.ElevatedColorStart.R | Should -Be 255
            [StatusLine.Prompt]::Configuration.ElevatedColorEnd.Name | Should -BeExactly 'Red'
            { [StatusLine.Prompt]::Configure(@{ ElevatedColorEnd = $null }) } | Should -Throw
            [StatusLine.Prompt]::Configure(@{ ElevatedColorStart = $null; ElevatedColorEnd = $null })
            [StatusLine.Prompt]::Configuration.ElevatedColorStart | Should -BeNullOrEmpty
            [StatusLine.Prompt]::Configuration.ElevatedColorEnd | Should -BeNullOrEmpty
        }
        finally {
            Disable-Prompt
        }
    }
}
