Describe 'Build launcher exit contract' -Tag Build {
    It 'excludes built-in parameter descriptions from command-help sources' {
        $builtIn = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
            @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters) + 'CommonParameters'
        foreach ($page in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..' 'docs' 'en-US') -Filter '*.md') {
            $content = Get-Content -LiteralPath $page.FullName -Raw
            foreach ($heading in [regex]::Matches($content, '(?m)^### -?([A-Za-z]+)\r?$')) {
                $builtIn | Should -Not -Contain $heading.Groups[1].Value -Because "$($page.Name) only authors module parameters"
            }
        }
    }

    It 'propagates task success and failure with CI=<CiValue>' -ForEach @(
        @{ CiValue = '' }
        @{ CiValue = 'true' }
    ) {
        $savedCi = $env:CI
        $powerShellPath = Join-Path $PSHOME 'pwsh.exe'
        if (-not $IsWindows) { $powerShellPath = Join-Path $PSHOME 'pwsh' }
        try {
            $env:CI = $CiValue
            & $powerShellPath -NoProfile -File "$PSScriptRoot/../build.ps1" -Task '?' *> $null
            $LASTEXITCODE | Should -Be 0
            & $powerShellPath -NoProfile -File "$PSScriptRoot/../build.ps1" -Task MissingReviewTask *> $null
            $LASTEXITCODE | Should -Be 1
        }
        finally { $env:CI = $savedCi }
    }
}
