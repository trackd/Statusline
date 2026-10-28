BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
    if (-not ('RefreshProbe' -as [type])) {
        Add-Type -Path "$PSScriptRoot/RefreshProbe.cs"
    }
}

Describe 'Bounded segment refresh' -Tag Lifecycle {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'cancels a native process without leaving it running' {
        $marker = Join-Path $TestDrive 'process-pid'
        [RefreshProbe]::CheckProcessAsync([StatusLine.StatusLineBlock].Assembly, [Environment]::ProcessPath, $marker).GetAwaiter().GetResult() | Should -BeExactly 'OK'
    }

    It 'keeps runtime versions separate for projects selecting different SDKs' -Skip:(-not (Get-Command dotnet -ErrorAction Ignore)) {
        $versions = @(dotnet --list-sdks | ForEach-Object { ($_ -split ' ')[0] })
        if ($versions.Count -lt 2) { Set-ItResult -Skipped -Because 'Two installed SDKs are needed.'; return }
        $directories = @()
        foreach ($index in 0, 1) {
            $directory = Join-Path $TestDrive "sdk-$index"
            $null = New-Item -Path $directory -ItemType Directory
            @{ sdk = @{ version = $versions[$index]; rollForward = 'disable' } } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'global.json')
            $directories += $directory
        }
        foreach ($index in 0, 1) {
            Push-Location -LiteralPath $directories[$index]
            try {
                $expected = $versions[$index]
                # Validate the SDK fixture and finish first-run setup before testing the refresh cache.
                $selectedVersion = (dotnet --version | Out-String).Trim()
                $LASTEXITCODE | Should -Be 0 -Because "SDK $expected must run in its fixture directory"
                $selectedVersion | Should -BeExactly $expected
                Wait-RefreshCondition { (New-TestBlock -Kind Dotnet -BlockTimeoutMilliseconds 5000).Text -eq $expected }
            }
            finally { Pop-Location }
        }
    }

    It 'omits an initially timed-out script and does not delay another block' {
        $registration = New-StatusLineBlock -Name timeoutOnly -ScriptBlock { Start-Sleep -Seconds 10; 'late' } -TimeoutMilliseconds 100 -IntervalMilliseconds 250
        try {
            @(New-TestBlock -Kind $registration).Count | Should -Be 0
            Wait-RefreshCondition { ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).IsStale }
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).LastError | Should -Match 'timed out'
            $quickAfterTimeout = New-StatusLineBlock -Name quickAfterTimeout -ScriptBlock { 'quick' }
            (New-TestBlock -Kind $quickAfterTimeout).Text | Should -BeExactly 'quick'
        }
        finally {
            $registration.Dispose()
            $quickAfterTimeout.Dispose()
        }
    }

    It 'marks stale values after a script timeout and clears the marker after recovery' {
        $modePath = Join-Path $TestDrive 'refresh-mode'
        Set-Content -LiteralPath $modePath -Value 'ready'
        $scriptText = @'
if ((Get-Content -LiteralPath '__PATH__' -Raw).Trim() -eq 'slow') { Start-Sleep -Seconds 10 }
'ready'
'@
        $scriptBlock = [scriptblock]::Create($scriptText.Replace('__PATH__', $modePath.Replace("'", "''")))
        $registration = New-StatusLineBlock -Name staleRecovery -ScriptBlock $scriptBlock -TimeoutMilliseconds 300 -IntervalMilliseconds 250
        try {
            (New-TestBlock -Kind $registration).Text | Should -BeExactly 'ready'
            $lastUpdated = ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).LastUpdated
            Set-Content -LiteralPath $modePath -Value 'slow'
            Wait-RefreshCondition { $null = New-TestBlock -Kind $registration; ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).IsStale }
            $stale = New-TestBlock -Kind $registration
            $stale.Text | Should -BeExactly 'ready [stale]'
            $stale.IsStale | Should -BeTrue
            $stale.LastError | Should -Match 'timed out'
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).LastUpdated | Should -Be $lastUpdated
            Set-Content -LiteralPath $modePath -Value 'ready'
            Wait-RefreshCondition { $null = New-TestBlock -Kind $registration; -not ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).IsStale }
            (New-TestBlock -Kind $registration).Text | Should -BeExactly 'ready'
        }
        finally { $registration.Dispose() }
    }

    It 'records nonterminating errors even when a script also writes output' {
        $registration = New-StatusLineBlock -Name mixedFailure -ScriptBlock { 'partial'; Write-Error 'mixed error' }
        try {
            $null = New-TestBlock -Kind $registration
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).LastError | Should -Match 'mixed error'
            ([StatusLine.RefreshDiagnostics]::GetScriptStatus($registration)).IsStale | Should -BeTrue
        }
        finally { $registration.Dispose() }
    }

    It 'validates timeout configuration' {
        { $invalidTimeout = New-StatusLineBlock -Name invalidTimeout -ScriptBlock { 'x' } -TimeoutMilliseconds 0 } | Should -Throw
        { Get-TestPrompt -Configuration ((New-TestPromptConfiguration @{BlockTimeoutMilliseconds = 0})) } | Should -Throw
    }
}
