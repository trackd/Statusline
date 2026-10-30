BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Command help sources' -Tag Contract {
    # Describe bodies run during discovery, so -ForEach can use this list.
    $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot '..' 'module' 'StatusLine.psd1')
    $commands = @($manifest.CmdletsToExport | ForEach-Object { @{ Name = $_ } })

    BeforeAll {
        $builtIn = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
            @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
        $docs = Join-Path $PSScriptRoot '..' 'docs' 'en-US'
    }

    It 'documents every module parameter of <Name> with at least two examples' -ForEach $commands {
        $page = Join-Path $docs "$Name.md"
        Test-Path -LiteralPath $page | Should -BeTrue
        $content = Get-Content -LiteralPath $page -Raw
        foreach ($parameter in (Get-Command $Name).Parameters.Keys | Where-Object { $_ -notin $builtIn }) {
            $content | Should -Match "(?m)^### -$parameter\r?$" -Because "$Name.md documents -$parameter"
        }
        [regex]::Matches($content, '(?m)^### Example \d+').Count | Should -BeGreaterOrEqual 2
    }
}
