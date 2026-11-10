BeforeAll {
    . "$PSScriptRoot/TestHelpers.ps1"
}

Describe 'Block creation and providers' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'uses a standard memory emoji distinct from the CPU chip' {
        (New-TestBlock -Kind SystemMemory -IconSet NerdFont).Icon | Should -BeExactly '🧠'
        (New-TestBlock -Kind SystemMemory -IconSet Unicode).Icon | Should -BeExactly '🧠'
        (New-TestBlock -Kind Cpu -IconSet NerdFont).Icon | Should -Not -Be '🧠'
    }

    It 'exports the construction cmdlet without registration cmdlets' {
        $module = Get-Module StatusLine
        $module.ExportedCmdlets.Keys | Should -Contain 'Enable-StatusLine'
        $module.ExportedCmdlets.Keys | Should -Contain 'New-StatusLineBlock'
        $module.ExportedCmdlets.Keys | Should -Not -Contain 'Register-StatusLineBlock'
        $module.ExportedCmdlets.Keys | Should -Not -Contain 'Unregister-StatusLineBlock'
    }

    It 'converts named, hex, and object colors to RGB styles' {
        $block = New-TestBlock 'hello' -Foreground '#C0C030' -Background MidnightBlue -Bold
        $block.Style | Should -BeExactly "$escape[0;38;2;192;192;48;48;2;25;25;112;1m"
        (New-TestBlock 'hello' -Foreground ([Drawing.Color]::FromArgb(1, 2, 3))).Style |
            Should -BeExactly "$escape[0;38;2;1;2;3;49m"
    }

    It 'uses terminal defaults for omitted colors' {
        (New-TestBlock 'hello').Style | Should -BeExactly "$escape[0;39;49m"
    }

    It 'adds italic and underline codes' {
        (New-TestBlock 'hello' -Italic -Underline).Style | Should -BeExactly "$escape[0;39;49;3;4m"
    }

    It 'rejects malformed colors' {
        { New-TestBlock 'hello' -Foreground '#GGGGGG' } | Should -Throw
        { New-TestBlock 'hello' -Foreground 'not-a-color' } | Should -Throw
    }

    It 'rejects invalid alignment' {
        { New-TestBlock 'hello' -Alignment Middle } | Should -Throw
    }

    It 'completes named colors' {
        $completionText = '$sample = New-StatusLineBlock -Name sample -ScriptBlock { 1 } -Foreground Mid'
        $completion = [Management.Automation.CommandCompletion]::CompleteInput($completionText, $completionText.Length, $null)
        $completion.CompletionMatches.CompletionText | Should -Contain 'MidnightBlue'
    }

    It 'creates context blocks with color overrides' {
        $blocks = @(New-TestBlock -Kind Directory, PowerShell, ProcessMemory, Clock, User, Host -Foreground Coral -Alignment Right)
        $blocks.Count | Should -Be 6
        $blocks.Alignment | Should -Not -Contain 'Left'
        $blocks[0].Style | Should -BeExactly "$escape[0;38;2;255;127;80;49m"
        $blocks[2].Text | Should -Match '^\d+ MiB$'
        $blocks[2].Icon | Should -Not -BeNullOrEmpty
    }

    It 'uses ASCII labels with the None icon set' {
        $block = New-TestBlock -Kind ProcessMemory -IconSet None
        $block.Content | Should -Match '^PROC \d+ MiB$'
    }

    It 'honors explicit success and zero duration' {
        $blocks = @(New-TestBlock -Kind Status, Duration -ExitCode 0 -Duration ([TimeSpan]::Zero) -IconSet None)
        $blocks[0].Text | Should -Be 'OK'
        $blocks[1].Text | Should -Be '0ms'
        @(New-TestBlock -Kind Status).Count | Should -Be 0
    }

    It 'reports failing exit codes' {
        $block = New-TestBlock -Kind Status -ExitCode 3 -IconSet None
        $block.Content | Should -Be 'ERR 3'
    }

    It 'rejects unknown kinds' {
        { New-TestBlock -Kind Nope } | Should -Throw '*built-in kind*'
    }

    It 'creates uptime and date blocks' {
        (New-TestBlock -Kind Uptime).Text | Should -Match '^\d+[dh] '
        @(New-TestBlock -Kind Date).Count | Should -Be 1
    }

    It 'reports CPU usage on Windows and Linux' -Skip:($IsMacOS) {
        (New-TestBlock -Kind Cpu).Text | Should -Match '^\s*\d+%$'
    }

    It 'refreshes script definitions in the background' {
        $testblock = New-StatusLineBlock -Name testblock -ScriptBlock { param($state) "at $($state.Location)" } -Icon '*' -Foreground Gold -IntervalMilliseconds 250
        try {
            $block = New-TestBlock -Kind $testblock -Padding ' '
            $block.Content | Should -BeExactly "* at $($PWD.Path)"
            $block.Alignment | Should -Be 'Right'
            $block.Style | Should -BeExactly "$escape[0;38;2;255;215;0;49m"
        }
        finally { $testblock.Dispose() }
        { New-TestBlock -Kind testblock } | Should -Throw
    }

    It 'passes blocks returned by a script definition through' {
        $styled = New-StatusLineBlock -Name styled -ScriptBlock { [StatusLine.BlockSnapshots]::Create('S', $null, $null, 'Left', '', $true) }
        try { (New-TestBlock -Kind $styled).Bold | Should -BeTrue }
        finally { $styled.Dispose() }
    }

    It 'shows script definition errors as a block' {
        $broken = New-StatusLineBlock -Name broken -ScriptBlock { throw 'boom' }
        try { (New-TestBlock -Kind $broken).Text | Should -BeLike '*boom*' }
        finally { $broken.Dispose() }
    }

    It 'keeps names descriptive without registering them for lookup' {
        $definition = New-StatusLineBlock -Name Clock -ScriptBlock { 'custom' }
        try {
            (New-TestBlock -Kind $definition).Text | Should -Be 'custom'
            (New-TestBlock -Kind Clock).Text | Should -Not -Be 'custom'
        }
        finally { $definition.Dispose() }
    }
    It 'reads branches from local Git metadata' {
        $gitDirectory = [IO.Path]::Combine($TestDrive, 'metadata', '.git')
        [void][IO.Directory]::CreateDirectory($gitDirectory)
        [IO.File]::WriteAllText([IO.Path]::Combine($gitDirectory, 'HEAD'), 'ref: refs/heads/feature/status')
        Push-Location -LiteralPath (Split-Path $gitDirectory)
        try { (New-TestBlock -Kind Git -Padding '').Text | Should -Be 'feature/status' }
        finally { Pop-Location }
    }

    It 'adds working tree counts from git status' -Skip:(-not (Get-Command git -CommandType Application -ErrorAction Ignore)) {
        $repository = [IO.Directory]::CreateDirectory([IO.Path]::Combine($TestDrive, 'repository')).FullName
        git -C $repository init --quiet --initial-branch main 2>$null
        # Avoid user excludes (e.g. a global *.txt ignore) hiding the untracked file.
        [IO.File]::WriteAllText([IO.Path]::Combine($repository, 'untracked.contourtest'), 'x')
        Push-Location -LiteralPath $repository
        try { (New-TestBlock -Kind Git -IconSet Unicode).Text | Should -Be 'main ?1' }
        finally { Pop-Location }
    }

    It 'skips Git outside the FileSystem provider' {
        Push-Location -LiteralPath 'Env:'
        try { @(New-TestBlock -Kind Git).Count | Should -Be 0 }
        finally { Pop-Location }
    }

    It 'selects the Nerd Font Git icon for <Provider>' -ForEach @(
        @{ Provider = 'GitHub'; RemoteUrl = 'git@github.com:owner/repo.git'; Code = 0xF09B }
        @{ Provider = 'GitLab'; RemoteUrl = 'https://gitlab.com/owner/repo.git'; Code = 0xF296 }
        @{ Provider = 'Azure DevOps'; RemoteUrl = 'git@ssh.dev.azure.com:v3/org/project/repo'; Code = 0xF0FD5 }
        @{ Provider = 'legacy Azure DevOps'; RemoteUrl = 'https://org.visualstudio.com/project/_git/repo'; Code = 0xF0FD5 }
        @{ Provider = 'unknown host'; RemoteUrl = 'https://github.com.example.org/owner/repo.git'; Code = 0xF062C }
        @{ Provider = 'no origin'; RemoteUrl = ''; Code = 0xF062C }
    ) -Skip:(-not (Get-Command git -CommandType Application -ErrorAction Ignore)) {
        $repository = Join-Path $TestDrive $Provider
        $null = New-Item -ItemType Directory -Path $repository
        git -C $repository init --quiet --initial-branch main
        if ($RemoteUrl) { git -C $repository remote add origin $RemoteUrl }
        Push-Location -LiteralPath $repository
        try {
            (New-TestBlock -Kind Git -IconSet NerdFont).Icon | Should -BeExactly ([char]::ConvertFromUtf32($Code))
        }
        finally { Pop-Location }
    }

    It 'pins Git commands despite inherited repository paths and spaces' -Skip:(-not (Get-Command git -CommandType Application -ErrorAction Ignore)) {
        $repository = New-Item -Path (Join-Path $TestDrive 'actual repository') -ItemType Directory
        $other = New-Item -Path (Join-Path $TestDrive 'other repository') -ItemType Directory
        git -C $repository.FullName init --quiet --initial-branch main
        git -C $other.FullName init --quiet --initial-branch other
        Set-Content -LiteralPath (Join-Path $repository.FullName 'real.contourtest') -Value 'content'
        $previousGitDirectory = $env:GIT_DIR
        $previousGitWorkTree = $env:GIT_WORK_TREE
        Push-Location -LiteralPath $repository.FullName
        try {
            $env:GIT_DIR = Join-Path $other.FullName '.git'
            $env:GIT_WORK_TREE = $other.FullName
            (New-TestBlock -Kind Git -IconSet Unicode).Text | Should -Be 'main ?1'
        }
        finally {
            $env:GIT_DIR = $previousGitDirectory
            $env:GIT_WORK_TREE = $previousGitWorkTree
            Pop-Location
        }
    }
}

Describe 'Environment and memory providers' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'renders or omits <Kind> according to available context' -ForEach @(
        @{ Kind = 'Aws'; Variable = 'AWS_PROFILE'; Value = 'test-profile'; Expected = 'test-profile' }
        @{ Kind = 'Aws'; Variable = 'AWS_PROFILE'; Value = ''; Expected = $null }
        @{ Kind = 'Azure'; Variable = 'AZURE_SUBSCRIPTION_ID'; Value = 'test-subscription'; Expected = 'test-subscription' }
        @{ Kind = 'Azure'; Variable = 'AZURE_SUBSCRIPTION_ID'; Value = ''; Expected = $null }
        @{ Kind = 'VirtualEnv'; Variable = 'VIRTUAL_ENV'; Value = '/tmp/test-environment'; Expected = 'test-environment' }
        @{ Kind = 'VirtualEnv'; Variable = 'VIRTUAL_ENV'; Value = ''; Expected = $null }
    ) {
        $names = @('AWS_PROFILE', 'AWS_DEFAULT_PROFILE', 'AZURE_SUBSCRIPTION_ID', 'VIRTUAL_ENV', 'CONDA_DEFAULT_ENV')
        $previous = @{}
        foreach ($name in $names) {
            $previous[$name] = [Environment]::GetEnvironmentVariable($name)
            [Environment]::SetEnvironmentVariable($name, $null)
        }
        try {
            [Environment]::SetEnvironmentVariable($Variable, $Value)
            $blocks = @(New-TestBlock -Kind $Kind)
            if ($null -eq $Expected) {
                $blocks.Count | Should -Be 0
            }
            else {
                $blocks.Count | Should -Be 1
                $blocks[0].Text | Should -BeExactly $Expected
            }
        }
        finally {
            foreach ($name in $names) {
                [Environment]::SetEnvironmentVariable($name, $previous[$name])
            }
        }
    }

    It 'reads package versions from structured project metadata' {
        $project = New-Item -Path (Join-Path $TestDrive 'package-project') -ItemType Directory
        '{"name":"example","version":"2.3.4"}' | Set-Content (Join-Path $project 'package.json')
        Push-Location $project
        try { (New-TestBlock -Kind Package).Text | Should -Be '2.3.4' }
        finally { Pop-Location }
    }

    It 'reports process working set independently of system usage' {
        (New-TestBlock -Kind ProcessMemory -IconSet None).Content | Should -Match '^PROC \d+ MiB$'
        (New-TestBlock -Kind SystemMemory -IconSet None).Content | Should -Match '^RAM \d+\.\d/\d+\.\d GiB \(\d+%\)$'
    }

}

Describe 'Status-Line Groups and Lifecycle' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    It 'creates snapshot blocks with explicit alignment groups' {
        $blocks = @(New-TestBlock -Kind Os -Left User -Center PowerShell -Right Host)
        $blocks.Count | Should -Be 4
        $blocks[1].Alignment | Should -Be 'Left'
        $blocks[2].Alignment | Should -Be 'Center'
        $blocks[3].Alignment | Should -Be 'Right'
    }
}

Describe 'Directory display and terminal-specific integration' -Tag Contract {
    BeforeEach { $savedTerminalEnvironment = Enter-TestTerminalEnvironment }

    AfterEach { Restore-TestTerminalEnvironment -SavedEnvironment $savedTerminalEnvironment }

    BeforeEach {
        $directoryRoot = (Get-Item -LiteralPath TestDrive:\).FullName
        $savedStyle = [StatusLine.DirectoryDisplay]::Style
        $savedBase = [StatusLine.DirectoryDisplay]::BasePath
        $savedMaximum = [StatusLine.DirectoryDisplay]::MaximumCharacters
        $savedComponents = [StatusLine.DirectoryDisplay]::MaximumComponents
        [StatusLine.DirectoryDisplay]::MaximumComponents = 3
        $savedTerminal = $env:TERM_PROGRAM
        $savedWtSession = $env:WT_SESSION
        $env:WT_SESSION = $null
    }

    AfterEach {
        [StatusLine.DirectoryDisplay]::Style = $savedStyle
        [StatusLine.DirectoryDisplay]::BasePath = $savedBase
        [StatusLine.DirectoryDisplay]::MaximumCharacters = $savedMaximum
        [StatusLine.DirectoryDisplay]::MaximumComponents = $savedComponents
        $env:TERM_PROGRAM = $savedTerminal
        $env:WT_SESSION = $savedWtSession
    }

    It 'uses a stable startup base and relative paths across location changes' {
        [StatusLine.DirectoryDisplay]::Style | Should -Be Relative
        $startup = [StatusLine.DirectoryDisplay]::StartupDirectory
        [StatusLine.DirectoryDisplay]::BasePath = $directoryRoot
        $child = [IO.Directory]::CreateDirectory((Join-Path $directoryRoot 'child folder')).FullName
        Push-Location -LiteralPath $child
        try {
            (New-TestBlock -Kind Directory -IconSet None).Text | Should -BeExactly 'child folder'
            [StatusLine.DirectoryDisplay]::StartupDirectory | Should -BeExactly $startup
            [StatusLine.DirectoryDisplay]::Format($directoryRoot) | Should -BeExactly ([IO.Path]::GetFileName([IO.Path]::TrimEndingDirectorySeparator($directoryRoot)))
            [StatusLine.DirectoryDisplay]::Format((Split-Path $directoryRoot)) | Should -BeExactly '..'
        }
        finally { Pop-Location }
    }

    It 'keeps only the last three relative components with the platform separator' {
        [StatusLine.DirectoryDisplay]::Style = 'Relative'
        [StatusLine.DirectoryDisplay]::BasePath = $directoryRoot
        $separator = [IO.Path]::DirectorySeparatorChar
        $shortPath = [IO.Path]::Combine($directoryRoot, 'one', 'two', 'three')
        [StatusLine.DirectoryDisplay]::Format($shortPath) | Should -BeExactly ("one${separator}two${separator}three")
        $longPath = [IO.Path]::Combine($directoryRoot, 'one', 'two', 'three', 'four', 'five')
        [StatusLine.DirectoryDisplay]::Format($longPath) | Should -BeExactly ("..${separator}three${separator}four${separator}five")
    }
    It 'allows a configurable relative depth and unlimited paths' {
        [StatusLine.DirectoryDisplay]::Style = 'Relative'
        [StatusLine.DirectoryDisplay]::BasePath = $directoryRoot
        $separator = [IO.Path]::DirectorySeparatorChar
        $path = [IO.Path]::Combine($directoryRoot, 'one', 'two', 'three')
        [StatusLine.DirectoryDisplay]::MaximumComponents = 2
        [StatusLine.DirectoryDisplay]::Format($path) | Should -BeExactly "..${separator}two${separator}three"
        [StatusLine.DirectoryDisplay]::MaximumComponents = 1
        [StatusLine.DirectoryDisplay]::Format($path) | Should -BeExactly "..${separator}three"
        [StatusLine.DirectoryDisplay]::MaximumComponents = 0
        [StatusLine.DirectoryDisplay]::Format($path) | Should -BeExactly "one${separator}two${separator}three"
        { [StatusLine.DirectoryDisplay]::MaximumComponents = -1 } | Should -Throw
    }
    It 'supports full, leaf, compact and short paths with native fallbacks' {
        $path = [IO.Directory]::CreateDirectory((Join-Path $directoryRoot 'a long directory name')).FullName
        [StatusLine.DirectoryDisplay]::Style = 'Full'
        [StatusLine.DirectoryDisplay]::Format($path) | Should -BeExactly $path
        [StatusLine.DirectoryDisplay]::Style = 'Leaf'
        [StatusLine.DirectoryDisplay]::Format($path) | Should -BeExactly 'a long directory name'
        [StatusLine.DirectoryDisplay]::Style = 'Compact'
        [StatusLine.DirectoryDisplay]::MaximumCharacters = 24
        [StatusLine.Unicode]::Measure([StatusLine.DirectoryDisplay]::Format($path)).Columns | Should -BeLessOrEqual 24
        [StatusLine.DirectoryDisplay]::Style = 'Short'
        [IO.Directory]::Exists([StatusLine.DirectoryDisplay]::Format($path)) | Should -BeTrue
    }
}

Describe 'Import folder home display' -Tag Contract {
    BeforeEach {
        $savedDirectoryStyle = [StatusLine.DirectoryDisplay]::Style
        $savedBasePath = [StatusLine.DirectoryDisplay]::BasePath
        $savedDepth = [StatusLine.DirectoryDisplay]::MaximumComponents
        [StatusLine.DirectoryDisplay]::Style = 'Relative'
        [StatusLine.DirectoryDisplay]::BasePath = $null
        [StatusLine.DirectoryDisplay]::MaximumComponents = 2
    }
    AfterEach {
        [StatusLine.DirectoryDisplay]::Style = $savedDirectoryStyle
        [StatusLine.DirectoryDisplay]::BasePath = $savedBasePath
        [StatusLine.DirectoryDisplay]::MaximumComponents = $savedDepth
    }

    It 'replaces the import folder name with the requested home icon' {
        $homePath = [StatusLine.DirectoryDisplay]::StartupDirectory
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @('Directory'); IconSet = 'NerdFont'; Padding = ''
            State = [StatusLine.StatusLineState]@{ Location = $homePath; FileSystemPath = $homePath }
        }
        $block = [StatusLine.BlockSnapshots]::Snapshot($options)[0]
        $block.Icon | Should -BeExactly ([string][char]0xe617)
        $block.Text | Should -BeExactly ''
        $block.Content | Should -BeExactly ([string][char]0xe617)
    }

    It 'preserves normal relative directory rendering below the startup folder' {
        $homePath = [StatusLine.DirectoryDisplay]::StartupDirectory
        $child = [IO.Path]::Combine($homePath, 'one', 'two', 'three')
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @('Directory'); IconSet = 'NerdFont'; Padding = ''
            State = [StatusLine.StatusLineState]@{ Location = $child; FileSystemPath = $child }
        }
        $block = [StatusLine.BlockSnapshots]::Snapshot($options)[0]
        $separator = [IO.Path]::DirectorySeparatorChar
        $block.Icon | Should -BeExactly ([string][char]0xf07c)
        $block.Text | Should -BeExactly "..${separator}two${separator}three"
        $options = [StatusLine.BlockSnapshotOptions]@{
            Block = @('Directory'); IconSet = 'None'; Padding = ''
            State = [StatusLine.StatusLineState]@{ Location = $homePath; FileSystemPath = $homePath }
        }
        [StatusLine.BlockSnapshots]::Snapshot($options)[0].Content | Should -BeExactly '~'
    }
}
