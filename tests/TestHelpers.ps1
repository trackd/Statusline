Import-Module -Name ($env:STATUSLINE_MODULE ?? "$PSScriptRoot/../output/StatusLine.psd1") -ErrorAction Stop
$escape = [string][char]27

# Private test adapters exercise the public .NET API without adding module commands.
function New-TestBlock {
    [CmdletBinding(DefaultParameterSetName = 'Text')]
    param(
        [Parameter(Mandatory, Position = 0, ParameterSetName = 'Text')]
        [AllowEmptyString()]
        [string]$Text,
        [Parameter(ParameterSetName = 'Text')]
        [string]$Icon = '',
        [Parameter(ParameterSetName = 'Text')]
        [switch]$Bold,
        [Parameter(ParameterSetName = 'Text')]
        [switch]$Italic,
        [Parameter(ParameterSetName = 'Text')]
        [switch]$Underline,
        [Parameter(ParameterSetName = 'Kind')]
        [object[]]$Kind,
        [Parameter(ParameterSetName = 'Kind')]
        [AllowEmptyCollection()]
        [object[]]$Left,
        [Parameter(ParameterSetName = 'Kind')]
        [AllowEmptyCollection()]
        [object[]]$Center,
        [Parameter(ParameterSetName = 'Kind')]
        [AllowEmptyCollection()]
        [object[]]$Right,
        [Parameter(ParameterSetName = 'Kind')]
        [object]$StartColor,
        [Parameter(ParameterSetName = 'Kind')]
        [object]$EndColor,
        [Parameter(ParameterSetName = 'Kind')]
        [int]$ExitCode,
        [Parameter(ParameterSetName = 'Kind')]
        [TimeSpan]$Duration,
        [Parameter(ParameterSetName = 'Kind')]
        [StatusLine.IconSet]$IconSet,
        [Parameter(ParameterSetName = 'Kind')]
        [int]$BlockTimeoutMilliseconds,
        [StatusLine.StatusLineAlignment]$Alignment = 'Left',
        [Alias('Color')]
        [object]$Foreground,
        [object]$Background,
        [AllowEmptyString()][string]$Padding = ''
    )
    if ($PSCmdlet.ParameterSetName -eq 'Text') {
        return [StatusLine.BlockSnapshots]::Create($Text, $Foreground, $Background, $Alignment, $Padding, $Bold.IsPresent, $Italic.IsPresent, $Underline.IsPresent, $Icon)
    }
    $settings = @{
        Block = @()
        Left = $null
        Center = $null
        Right = $null
        Alignment = $null
        Foreground = $null
        Background = $null
        StartColor = $null
        EndColor = $null
        Padding = ' '
        IconSet = 'NerdFont'
        WeatherLocation = $null
        BlockTimeoutMilliseconds = 2000
        ExitCode = $null
        Duration = $null
        State = $null
    }
    foreach ($name in 'Kind', 'Left', 'Center', 'Right', 'StartColor', 'EndColor', 'ExitCode', 'Duration', 'IconSet', 'BlockTimeoutMilliseconds', 'Alignment', 'Foreground', 'Background', 'Padding') {
        if ($PSBoundParameters.ContainsKey($name)) {
            $settings[$(if ($name -eq 'Kind') { 'Block' } else { $name })] = $PSBoundParameters[$name]
        }
    }
    [StatusLine.BlockSnapshots]::Snapshot([StatusLine.BlockSnapshotOptions]$settings)
}

function Write-TestStatusLine {
    [CmdletBinding(DefaultParameterSetName = 'Text')]
    param(
        [Parameter(Mandatory, Position = 0, ParameterSetName = 'Text')]
        [AllowEmptyString()]
        [string]$Text,
        [Parameter(Mandatory, ValueFromPipeline, ParameterSetName = 'Block')]
        [AllowEmptyCollection()]
        [StatusLine.StatusLineBlock[]]$Block,
        [Parameter(Mandatory, ParameterSetName = 'Mode')]
        [StatusLine.StatusLineMode]$Mode,
        [Parameter(ParameterSetName = 'Block')]
        [int]$Width,
        [Parameter(ParameterSetName = 'Block')]
        [AllowEmptyString()]
        [switch]$BlockSeparator,
        [Parameter(ParameterSetName = 'Block')]
        [StatusLine.SegmentStyle]$Style,
        [Parameter(ParameterSetName = 'Block')]
        [object]$StartColor,
        [Parameter(ParameterSetName = 'Block')]
        [object]$EndColor,
        [switch]$BlendBackground,
        [switch]$AsEscapeSequence,
        [switch]$Force
    )
    begin {
        $collectedBlocks = [Collections.Generic.List[StatusLine.StatusLineBlock]]::new()
    }
    process {
        if ($PSCmdlet.ParameterSetName -eq 'Block') {
            $collectedBlocks.AddRange($Block)
        }
    }
    end {
        if ($PSCmdlet.ParameterSetName -eq 'Mode') {
            if ($AsEscapeSequence.IsPresent) {
                return [StatusLine.TerminalControl]::ModeSequence($Mode)
            }
            [StatusLine.TerminalControl]::SetMode($Mode, $Force.IsPresent)
        }
        elseif ($PSCmdlet.ParameterSetName -eq 'Text') {
            if ($AsEscapeSequence.IsPresent) {
                return [StatusLine.TerminalControl]::TextSequence($Text)
            }
            [StatusLine.TerminalControl]::WriteText($Text, $Force.IsPresent)
        }
        else {
            $settings = @{ Width = 120; BlockSeparator = $false; Style = 'Plain'; StartColor = $null; EndColor = $null; BlendBackground = $false }
            foreach ($name in 'Width', 'BlockSeparator', 'Style', 'StartColor', 'EndColor', 'BlendBackground') {
                if ($PSBoundParameters.ContainsKey($name)) {
                    $settings[$(if ($name -eq 'Kind') { 'Block' } else { $name })] = $PSBoundParameters[$name]
                }
            }
            $options = [StatusLine.BlockRenderOptions]$settings
            if ($AsEscapeSequence.IsPresent) {
                return [StatusLine.TerminalControl]::BlocksSequence($collectedBlocks.ToArray(), $options)
            }
            [StatusLine.TerminalControl]::WriteBlocks($collectedBlocks.ToArray(), $options, $Force.IsPresent)
        }
    }
}

function Read-TestStatusFile {
    param([string]$Path)
    # Permit the producer's atomic replacement while retaining a complete opened version.
    $stream = [IO.File]::Open($Path, 'Open', 'Read', [IO.FileShare]'ReadWrite,Delete')
    $reader = [IO.StreamReader]::new($stream)
    try { $reader.ReadToEnd() }
    finally { $reader.Dispose() }
}

function Wait-RefreshCondition {
    param(
        [scriptblock]$Condition,
        [switch]$FlushHost
    )
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($FlushHost.IsPresent) {
            [StatusLine.StatusLineActivity]::FlushHost([runspace]::DefaultRunspace.InstanceId)
        }
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 25
    } while ($clock.Elapsed.TotalSeconds -lt 8)
    throw 'Refresh condition was not reached.'
}

# Test adapters keep assertion setup readable; these functions are not module exports.
function Enter-TestTerminalEnvironment {
    $savedEnvironment = @{}
    foreach ($name in 'TERM_PROGRAM', 'TERM_PROGRAM_VERSION', 'TERM', 'WT_SESSION', 'KITTY_PID', 'GHOSTTY_RESOURCES_DIR', 'TMUX', 'STY', 'VSCODE_SHELL_INTEGRATION', 'VSCODE_NONCE') {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }
    # Probe responses are cached by connection identity, including TERM. Each test gets its own.
    $env:TERM = 'statusline-test-' + [Guid]::NewGuid().ToString('N')
    $savedEnvironment
}

function Restore-TestTerminalEnvironment {
    param([hashtable]$SavedEnvironment)
    foreach ($name in $SavedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $SavedEnvironment[$name])
    }
}

function New-TestPromptConfiguration {
    param([hashtable]$Override = @{})

    # Test-owned settings: production defaults are user preferences, not test inputs.
    $settings = @{
        Left = @('Directory', 'Git', 'Status')
        Right = @('Duration')
        Block = @()
        Input = @()
        Style = 'Powerline'
        IconSet = 'NerdFont'
        ShellIntegration = 'None'
        Symbol = '> '
        ConnectRows = $true
        ContextSymbol = '╭─ '
        InputLinePrefix = '╰─ '
        TransientBlock = @()
        TransientSymbol = '> '
        ShellContext = $false
        TerminalProgress = $false
        DurationWarning = $false
        BlockSeparator = $false
        Padding = ''
        Transient = $false
        Rows = 1
        WeatherLocation = $null
        BlockTimeoutMilliseconds = 2000
        StartColor = $null
        EndColor = $null
        ElevatedColorStart = $null
        ElevatedColorEnd = $null
    }
    foreach ($name in $Override.Keys) {
        $settings[$name] = $Override[$name]
    }
    [StatusLine.PromptConfiguration]$settings
}

function Get-TestPrompt {
    param(
        [StatusLine.PromptConfiguration]$Configuration = (New-TestPromptConfiguration),
        [object[]]$Block,
        [int]$Width = 120,
        [int]$ExitCode,
        [TimeSpan]$Duration,
        [switch]$Transient,
        [switch]$IncludeIntegration
    )
    $selectedBlock = if ($PSBoundParameters.ContainsKey('Block')) { , $Block } else { $null }
    $selectedExitCode = if ($PSBoundParameters.ContainsKey('ExitCode')) { $ExitCode } else { $null }
    $selectedDuration = if ($PSBoundParameters.ContainsKey('Duration')) { $Duration } else { $null }
    [StatusLine.PromptPreview]::Render($Configuration, $selectedBlock, $Width, $selectedExitCode, $selectedDuration, $Transient.IsPresent, $IncludeIntegration.IsPresent)
}

function Get-TestTerminalCapability {
    param(
        [string]$Response,
        [switch]$AsEscapeSequence
    )
    if ($AsEscapeSequence.IsPresent) {
        return [StatusLine.TerminalControl]::StatusLineQuery
    }
    if ($PSBoundParameters.ContainsKey('Response')) {
        return [StatusLine.TerminalControl]::AcceptResponse($Response)
    }
    [StatusLine.TerminalControl]::Detect()
}

# Set an output fixture's policy explicitly instead of inheriting native console attachment.
# Production selects this policy before rendering; tests cover both paths independently.
function Set-TestHostDeferral {
    param([Parameter(Mandatory)][switch]$Deferred)

    $updaterType = [StatusLine.StatusLineBlock].Assembly.GetType('StatusLine.StatusLineUpdater')
    $flags = [Reflection.BindingFlags]'Static,NonPublic'
    $outputGate = $updaterType.GetField('OutputGate', $flags).GetValue($null)
    [Threading.Monitor]::Enter($outputGate)
    try {
        $output = $updaterType.GetField('currentOutput', $flags).GetValue($null)
        if ($null -eq $output) { throw 'Enable a statusline before configuring its test output policy.' }
        $field = $output.GetType().GetField('deferHost', [Reflection.BindingFlags]'Instance,NonPublic')
        $field.SetValue($output, $Deferred.IsPresent)
    }
    finally {
        [Threading.Monitor]::Exit($outputGate)
    }
}
