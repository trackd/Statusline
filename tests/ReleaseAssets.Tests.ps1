BeforeAll {
    $assetScript = Join-Path $PSScriptRoot '../.github/Get-ReleaseAssets.ps1'
}

Describe 'Release artifact selection' {
    BeforeEach {
        $assetPath = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $assetPath | Out-Null
        foreach ($name in 'statusline-0.2.0-linux-x64.tar.gz', 'statusline-0.2.0-linux-x64.deb', 'statusline-0.2.0-linux-x64.rpm', 'statusline-0.2.0-win-x64.zip', 'statusline-0.2.0-osx-arm64.tar.gz', 'statusline-0.2.0-osx-arm64.pkg') {
            $archivePath = Join-Path $assetPath $name
            [IO.File]::WriteAllText($archivePath, 'archive fixture')
            $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText("$archivePath.sha256", "$hash  $name`n")
        }
    }

    It 'selects native archives and Linux packages with checksums without requiring a module package' {
        $assets = @(& $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly)
        $assets.Count | Should -Be 12
        $assets.Name | Should -Contain 'statusline-0.2.0-osx-arm64.pkg'
        $assets.Name | Should -Contain 'statusline-0.2.0-linux-x64.deb'
        $assets.Name | Should -Contain 'statusline-0.2.0-linux-x64.rpm'
        $assets.Name | Should -Contain 'statusline-0.2.0-linux-x64.tar.gz'
        $assets.Name | Should -Contain 'statusline-0.2.0-win-x64.zip'
        $assets.Name | Should -Contain 'statusline-0.2.0-osx-arm64.tar.gz'
    }

    It 'requires the module package for a synchronized release' {
        { & $assetScript -Version 0.2.0 -Path $assetPath } | Should -Throw '*StatusLine.0.2.0.nupkg*'
        [IO.File]::WriteAllText((Join-Path $assetPath 'StatusLine.0.2.0.nupkg'), 'module fixture')
        $assets = @(& $assetScript -Version 0.2.0 -Path $assetPath)
        $assets.Count | Should -Be 13
        $assets.Name | Should -Contain 'StatusLine.0.2.0.nupkg'
    }

    It 'rejects a missing Linux package or its checksum' {
        Remove-Item -LiteralPath (Join-Path $assetPath 'statusline-0.2.0-linux-x64.rpm.sha256')
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*rpm.sha256*'
    }

    It 'requires the macOS installer and its checksum' {
        Remove-Item -LiteralPath (Join-Path $assetPath 'statusline-0.2.0-osx-arm64.pkg.sha256')
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*pkg.sha256*'
    }

    It 'rejects missing targets and version mismatches' {
        Remove-Item -LiteralPath (Join-Path $assetPath 'statusline-0.2.0-win-x64.zip')
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*win-x64.zip*'
        { & $assetScript -Version 0.3.0 -Path $assetPath -NativeOnly } | Should -Throw '*0.3.0*'
    }

    It 'rejects corrupted archives before returning release assets' {
        [IO.File]::WriteAllText((Join-Path $assetPath 'statusline-0.2.0-linux-x64.tar.gz'), 'changed')
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*Invalid checksum*'
    }

    It 'rejects duplicate and unexpected assets' {
        $duplicatePath = Join-Path $assetPath 'duplicate'
        New-Item -ItemType Directory -Path $duplicatePath | Out-Null
        Copy-Item -LiteralPath (Join-Path $assetPath 'statusline-0.2.0-win-x64.zip') -Destination $duplicatePath
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*found 2*'
        Remove-Item -LiteralPath (Join-Path $duplicatePath 'statusline-0.2.0-win-x64.zip')
        [IO.File]::WriteAllText((Join-Path $assetPath 'unexpected.zip'), 'unexpected')
        { & $assetScript -Version 0.2.0 -Path $assetPath -NativeOnly } | Should -Throw '*unexpected files*'
    }
}
