# Configuration guide

See the [README](../README.md) for a quick start and the [command reference](en-US/StatusLine.md)
for every parameter. Importing the module only makes the commands available; it does not change
your prompt, key bindings or terminal until you enable a surface.

Preview settings before enabling them:

```powershell
$configuration = [StatusLine.PromptConfiguration]@{
    Left = 'Directory', 'Git'
    Right = 'Status', 'Duration'
    IconSet = 'None'
}
Show-Prompt -Configuration $configuration -Width 100
Enable-Prompt -Configuration $configuration
```

Edit an enabled surface without replacing its hooks or worker:

```powershell
[StatusLine.Prompt]::Settings.Symbol = '> '
[StatusLine.Prompt]::Settings.Left = 'Directory', 'Git', 'Errors'
```

`[StatusLine.StatusBar]::Settings` provides the same property-editing model for an enabled
statusline. `Configuration` still returns a detached snapshot. Invalid edits leave the active
settings intact; lifecycle options such as output target require re-enabling.


To keep your setup between sessions, add `Import-Module StatusLine` and your Enable commands to
your PowerShell profile.

## Commands

| Command | Purpose |
| --- | --- |
| Enable-Prompt / Disable-Prompt | Install the prompt, or restore the previous one |
| Enable-StatusLine / Disable-StatusLine | Start or stop the live status line |
| New-StatusLineBlock | Create a reusable built-in or script-backed block with optional styling |
| Show-Prompt / Show-StatusLine | Preview a configuration without installing it |
| Measure-Codepoint | Inspect Unicode grapheme count and terminal width |

Enable and Disable commands support `-WhatIf` and `-Confirm`. WhatIf does not install hooks,
start workers, write files or change shared directory settings.

## Prompt

```powershell
$promptOptions = @{
    Left = 'Directory', 'Git'
    Right = 'Duration', 'Clock'
    StartColor = '#275DAD'
    EndColor = '#E8A33D'
    Transient = $true
}
Enable-Prompt @promptOptions
Disable-Prompt
```

The default prompt shows Directory, Git, Status and Duration in the left group. Blocks with no
value are omitted. `Disable-Prompt` restores the previous prompt and reader only if this module
still owns them, so later changes made by another tool are kept. PSReadLine must be loaded.

Supplying only `-Left` or `-Right` leaves the other group empty. With `-Configuration`, omitted
groups keep the configuration's settings, and `-Right @()` explicitly clears the right group.

### Rows and symbols

`Rows` selects one or two rows. Selecting any `Right` or `Input` blocks forces a context row above
editable input, even with `-Rows 1`. `Input` places blocks beside the input marker.

| Setting | Purpose | Default |
| --- | --- | --- |
| `Rows` | One or two prompt rows | 1 |
| `Symbol` | Marker immediately before typed input | One space |
| `TransientSymbol` | Compact marker left in history; also used for an otherwise empty input row | `{{E285}} ` |
| `ConnectRows` | Show the row connectors below | Off |
| `ContextSymbol` | Prefix of the upper row when `ConnectRows` is on | `╭─ ` |
| `InputLinePrefix` | Prefix of the lower row when `ConnectRows` is on | `╰─ ` |
| `Input` | Blocks before the input marker | Empty |

```powershell
$configuration = [StatusLine.PromptConfiguration]@{
    Left = 'Directory', 'Git', 'VirtualEnv'
    Right = 'Status', 'Duration'
    Input = 'Errors', 'NestedPrompt'
    ConnectRows = $true
    Symbol = '❯ '
    TransientSymbol = '❯ '
}
Enable-Prompt -Configuration $configuration
```

On a two-row prompt without visible input-row blocks, a blank `Symbol` falls back to
`TransientSymbol`. Set both symbols empty to hide the input marker. An explicit
`-ConnectRows:$false` overrides the configuration.

### Transient prompt

`-Transient` replaces each accepted prompt with the compact `TransientSymbol`, keeping history
easy to read. `-TransientBlock` adds blocks before the symbol and turns transient mode on
automatically; `-Transient:$false` overrides that.

```powershell
Enable-Prompt -TransientBlock Duration
```

TransientBlock accepts built-in names and block definitions, in display order. The values are
captured when the full prompt is drawn, so the redraw does not query providers. The compact prompt
uses the gradient start color, or the first block's color from the full prompt.

The full prompt is kept when the redraw cannot be placed reliably: redirected output,
screen-reader mode, a resized terminal, a prompt scrolled off screen, or input with non-ASCII or
control characters whose wrapping is uncertain. Key bindings and custom Enter handlers are
unchanged, and the module never accepts the same input twice.

### Preview and live settings

```powershell
$configuration = [StatusLine.PromptConfiguration]@{
    Left = 'Directory', 'Git', 'Status'
    Right = 'Duration', 'Clock'
    Style = 'Powerline'
    StartColor = '#244A73'
    EndColor = '#62C4A6'
}
Show-Prompt -Configuration $configuration -Width 100
Enable-Prompt -Configuration $configuration
```

`Show-Prompt` and `Show-StatusLine` render a configuration without installing hooks, starting
the updater or writing a status file. Custom scripts and slow providers still run, so a first
preview can omit values that are not ready yet. `[StatusLine.PromptPreview]::Render($configuration)`
returns the same preview as a string.

See [Runtime changes](#runtime-changes) to edit an enabled surface.

## Status Line

```powershell
$statusLineOptions = @{
    Left = 'Directory', 'Git'
    Center = 'PowerShell'
    Right = 'SystemMemory', 'ProcessMemory', 'Clock'
    IntervalMilliseconds = 1000
    StartColor = '#275DAD'
    EndColor = '#E8A33D'
}
Enable-StatusLine @statusLineOptions
Disable-StatusLine
```

The status line defaults to Cpu, ProcessMemory and Clock. `-Block` keeps each block's natural
alignment; `-Left`, `-Center` and `-Right` override it. Specifying a group replaces the default
block list, and an explicit `-Block` is merged with the groups.

The center group stays centered on the terminal width as side groups change. Side groups are
clipped where they would overlap the center; without a center group, left blocks take priority.

The default `Host` target draws into the terminal's DEC host-writable status line, a separate row
that the terminal keeps below the screen (supported by Contour and mintty). Supported terminals are
detected from the environment and from query replies; use `-Force` for other compatible terminals.
`Disable-StatusLine` returns the terminal to its indicator display; `-Mode Hidden` hides the status
line instead. The status line belongs to the runspace that enabled it.

Terminal updates pause while a command runs, so they cannot interleave with its output. Collection
and file output continue. `-UpdateDuringCommand` keeps updating during commands, but concurrent
native or full-screen program output can then interleave with status line writes. While text is in
the input buffer, the status line keeps its last frame until the next prompt.

### Idle refresh

`-IdleTimeoutSeconds 60` is the default. After a minute without shell activity, rendering,
provider refreshes and file updates pause. Accepting a command (including a blank Enter) or the
next prompt resumes them. Typing into the input buffer alone does not resume updates, and running
commands are never treated as idle. Use `-IdleTimeoutSeconds 0` for continuous refreshes.

### Preserve Scrollback in Contour

Contour's `history.auto_scroll_on_update: true` jumps to the bottom on **any** screen update,
including status line refreshes. In your existing profile, change only this setting:

```yaml
profiles:
  main:
    history:
      auto_scroll_on_update: false
```

This also stops ordinary program output from jumping to the bottom; keyboard input still returns
to the input area. If you cannot change that setting, run `Disable-StatusLine` before reading
history and enable it again afterward, or use [file output](#file-output-and-contour-program).

### Prompt and Status Together

```powershell
Enable-Prompt -Left Directory,Git -Right Status,Duration -Transient
Enable-StatusLine -Left User,Host -Right SystemMemory,ProcessMemory,Clock
```

Providers and script caches are shared. The prompt captures command status, duration and location
once and passes them to the status line. Without the prompt, a small prompt wrapper still captures
the location and `$LASTEXITCODE`, but command duration is unavailable.

## Fitting the width

Rows never exceed the terminal width, and side groups never touch: left and right keep at least
one blank cell between them. When blocks do not fit:

1. The Directory block is shortened with a middle ellipsis, keeping more of the tail
   (`C:\Us…\project`), down to 12 cells of text.
2. Whole blocks are dropped from the end of the row. Right-group blocks go before left-group
   blocks, and the Directory block goes last.
3. A single block that still does not fit is clipped at a grapheme boundary.

Single-row prompts also keep `min(20, (width - 1) / 2)` cells free after the symbol for typing.
PSReadLine owns command editing and prediction layout.

## Appearance

### Segment styles

`-Style` offers Plain, Powerline, Rounded and Diamond:

| Style | Shape |
| --- | --- |
| Plain | Colored text, no fills |
| Powerline | Filled blocks joined by arrows (U+E0B0/U+E0B2) |
| Rounded | Filled blocks joined by half circles (U+E0B4/U+E0B6) |
| Diamond | Each block is its own rounded pill, with one cell between pills |

```powershell
Enable-Prompt -Style Rounded
Enable-StatusLine -Style Diamond
```

Powerline prompt groups get rounded outer edges: the left group starts with `{{E0B6}}` and the
right group ends with `{{E0B4}}`. Separator shapes require a patched Nerd Font; `-IconSet`
controls block icons independently.

`-BlockSeparator` draws transparent breaks between filled Powerline blocks, using U+E0D7 and
U+E0D6 from Powerline Extra. The cutout shows the terminal's default background, so background
images and transparency stay visible. Other styles ignore it.

`-Padding` wraps each block's content on both sides, inside its colored edges. It defaults to a
space and may be empty or a thin space (`[string][char]0x2009`), depending on the font. Icon
and label are separated by one ordinary space.

### Gradients

```powershell
Enable-StatusLine -Left User,Host,Os -StartColor '#225588' -EndColor '#88CCAA'
```

Supply **both** `StartColor` and `EndColor`. Colors accept names, `#RRGGBB` or Drawing.Color
values on parameters; configuration properties accept names and hex strings. The gradient covers
the blocks actually shown, so hidden blocks leave no gaps. One block uses the start color.

Plain style colors the text. Filled styles use the gradient as block backgrounds with
contrasting text. Colors set on a block definition take precedence over the gradient.

`-BlendBackground` paints one continuous gradient across the full row width, changing color
inside blocks and across gaps. Numbers such as `12.34`, `1,234.56` or `10%` keep a single text
color. BlendBackground is not used with `-BlockSeparator` or rows containing explicit block colors.

`ElevatedColorStart` and `ElevatedColorEnd` give elevated prompt sessions their own gradient,
including the input row and transient marker. Supply both or neither; unelevated sessions keep
StartColor and EndColor. No Elevation block is required.

```powershell
Enable-Prompt -StartColor '#0400fd' -EndColor '#ff7b00' -ElevatedColorStart '#ff7b00' -ElevatedColorEnd '#cc2200'
```

### Text contrast

Filled blocks default to `-TextContrast Automatic`: on an explicit RGB background the text is
black or white, whichever contrasts more. Palette backgrounds are theme-defined, so they keep the
terminal default foreground. An explicit block foreground is always respected.

`Preserve` keeps the block's foreground, and `Black` or `White` forces one. The setting applies
to filled blocks, including input-row blocks; unfilled input and PSReadLine syntax colors are not
changed. SGR embedded in custom block text can override the color; use semicolon-separated
parameters, as sequences with colon subparameters are neutralized.

## Directory display

```powershell
Enable-Prompt -DirectoryDepth 2
[StatusLine.DirectoryDisplay]::Style = 'Relative'   # default
[StatusLine.DirectoryDisplay]::BasePath = 'D:\Code' # $null restores the import directory
[StatusLine.DirectoryDisplay]::MaximumComponents = 2
```

The Directory block shows the path relative to the folder where the module was first imported.
That exact folder is shown as a home icon (`{{E617}}` with Nerd Font, `⌂` with Unicode, `~` with
IconSet None). Other folders on another Windows drive stay absolute, and provider locations such
as the registry keep their provider path.

`-DirectoryDepth` (on either Enable command) keeps that many trailing components, so longer paths
appear as `..\folder1\folder2\folder3`. The default is 3, and `0` keeps the complete path. The
setting is shared by the prompt and the status line; omitting the parameter keeps the current
value.

| Style | Display |
| --- | --- |
| Relative | Relative to the base directory (default) |
| Full | Complete path |
| Home | `~` for the user profile |
| Leaf | Folder name only |
| Compact | Shortened to `MaximumCharacters` |
| Short | Windows 8.3 short path, or the full path when unavailable |

These preferences are process-wide. When a row is still too narrow, the renderer shortens the
Directory block further (see [Fitting the width](#fitting-the-width)). Display formatting never
changes the working directory reported to the terminal.

## Available Blocks

| Kind | Data |
| --- | --- |
| Directory, Git | Location; branch, ahead/behind and working-tree counts |
| Status, Duration, HistoryId | Last command status; last command duration; prompt history ID |
| SystemMemory | Used/total physical memory in GiB and percentage |
| ProcessMemory | Current PowerShell process working set in MiB |
| Cpu, Network | System CPU usage; receive/send throughput |
| Gpu | GPU usage; per-adapter labels when several adapters report |
| Battery, Weather | Charge and charging state; weather from wttr.in |
| Errors, StackCount, NestedPrompt, Runspaces | Retained `$Error` records, location stack depth, nested prompt depth, process runspaces |
| PowerShell, User, Host, Os, Elevation | Session, platform and privilege information |
| Clock, Date, Uptime | Local time, date and system uptime |
| Node, Python, Dotnet, Go, Rust, Java, Ruby | Runtime versions for the current project |
| Package, VirtualEnv | package.json version; Python or Conda environment |
| Aws, Azure | Profile or subscription from environment settings; no cloud sign-in |
| Kubernetes, Docker | Local CLI context |

Blocks with nothing to show are omitted, and zero counts are hidden. Errors counts `$Error`
until you clear it. Network needs two samples before it reports a rate.

- **Duration** formats as `9ms`, `2.5s` or `1m15s`. `-DurationWarning` adds a red warning after
  Duration when the last command failed.
- **HistoryId** shows `{{F060D}}` and the history ID of the prompt being drawn.
- **Os** shows the installed release, such as `Windows 11 26H2` or `Debian GNU/Linux 13 (trixie)`.
- **Weather** is opt-in and requests data from wttr.in. `-WeatherLocation` chooses the location;
  otherwise wttr.in picks one.
- **SystemMemory** reports OS physical memory, not .NET heap sizes or container limits.
- **Gpu** is opt-in. It reads Windows performance counters, NVIDIA NVML or AMD sysfs on Linux,
  and Apple Silicon activity on macOS. Intel GPUs on Linux and Intel Macs are not supported.
  Windows and macOS need one sample before reporting, so the block appears on a later refresh.
- **Cpu** and **Battery** work on Windows, Linux and macOS; missing hardware is omitted.

Runtime and toolchain blocks are cached per project directory. Git refreshes every two seconds,
system metrics every second, runtime versions every ten seconds and weather every fifteen minutes.
Prompts never wait for providers, so a first render may omit a value that is still loading.

## Custom Refreshing Blocks

~~~powershell
$project = New-StatusLineBlock -Name ProjectLabel -ScriptBlock {
    param($state)
    if ($state.FileSystemPath) { Split-Path $state.FileSystemPath -Leaf }
} -IntervalMilliseconds 5000 -Icon ([char]0xF07C) -Foreground Gold

$duration = New-StatusLineBlock -Kind Duration -Foreground White -Background '#1034A6'
Enable-Prompt -Left @($project, 'Git') -Right $duration
Enable-StatusLine -Left $project -Right ProcessMemory,Clock
~~~

New-StatusLineBlock returns a reusable definition. It does not run the script or register its
name. Use the object in Block, Left, Center, Right, Input or TransientBlock. Explicit group
placement overrides the definition's alignment, and definition padding overrides the surface
padding. Use the same object in both surfaces to share its cached output.

A definition's kind, script, interval and timeout are fixed; create a new definition to change
them. Appearance properties can be edited and apply on the next render. `$null` inherits, an
empty Icon removes the icon and `$false` turns off an inherited attribute:

```powershell
$duration.Foreground = 'Cyan'
$duration.Bold = $false
$duration.Foreground = $null
```

Scripts run in a background runspace pool and cannot see the caller's variables; use the
`$state` argument instead. Only selected definitions refresh. Once a definition is no longer
selected, a refresh already running may finish, but no new one starts.

### The `$state` argument

A script receives a single argument, a `[StatusLine.StatusLineState]` record captured on the
foreground runspace just before the render that requested the refresh. It is a snapshot: values
do not change while the script runs, and each script receives its own copy.

| Property | Type | Value |
| --- | --- | --- |
| `Location` | `string` | PowerShell path of the current location, e.g. `C:\src` or `HKLM:\Software` |
| `FileSystemPath` | `string` | Provider path when the location is in the FileSystem provider; otherwise `$null` |
| `LastExitCode` | `int?` | Exit code of the last command (see below) |
| `LastDuration` | `TimeSpan?` | Duration of the last command; `$null` until a command completes under `Enable-Prompt` |
| `ErrorCount` | `int` | Number of records in `$global:Error` |
| `StackCount` | `int` | Number of locations on the default location stack (`Push-Location`) |
| `NestedPromptLevel` | `int` | `$NestedPromptLevel`; non-zero while debugging or in `$Host.EnterNestedPrompt()` |
| `RunspaceCount` | `int` | Runspaces in the process, including StatusLine's own workers |
| `HistoryId` | `long?` | History ID of the prompt being drawn; `$null` outside `Enable-Prompt` |

With `Enable-Prompt`, `LastExitCode` is 0 when the last command succeeded, otherwise the native
exit code, or 1 when the failure was a PowerShell error. Without the prompt (status line only) it is
`$global:LASTEXITCODE`. `Show-Prompt` and `Show-StatusLine` fill `LastExitCode` and `LastDuration`
from their `-ExitCode` and `-Duration` parameters, which makes them handy for testing a script:

~~~powershell
$probe = New-StatusLineBlock -Name Probe -ScriptBlock {
    param($state)
    "exit $($state.LastExitCode) in $($state.LastDuration) at $($state.Location)"
}
Show-Prompt -Configuration @{ Left = @($probe); Right = @() } -ExitCode 2 -Duration 0:0:3
~~~

Output rules:

- Every object written to the output stream becomes its own block, in order. `$null` is skipped
  and returning nothing hides the block.
- Objects are converted to text with PowerShell's string conversion when rendered. Return strings
  if you want the converted text cached between renders.
- A returned `[StatusLine.StatusLineBlock]` keeps its own text, colors and attributes; settings on
  the definition (Foreground, Icon, Bold, ...) still take precedence. Create one with
  `[StatusLine.BlockSnapshots]::Create('text', 'Gold', 'Black')`, which accepts the same color
  values as `New-StatusLineBlock` (names, `#RRGGBB`, `ConsoleColor`).
- Anything written to the error stream fails the refresh; the previous output is kept (shown as
  stale) or, without one, a warning block shows the first error.
- Scripts are rerun at most every `IntervalMilliseconds` (default 5000) and stopped after
  `TimeoutMilliseconds` (default 2000). Live prompt and status-line renders never wait for a
  script: they start a due refresh and show the last value, so a new block appears on the next
  render after its first run completes. `Show-Prompt` and `Show-StatusLine` are one-shot
  previews instead; they start every due refresh together and wait up to `BlockTimeoutMilliseconds`
  for the results.
- Output is cached per definition for `IntervalMilliseconds`, including in previews. Repeating a
  preview with a different `-ExitCode` inside that window shows the earlier result; use a short
  `-IntervalMilliseconds` (minimum 250) while experimenting.

## Refresh Deadlines and Diagnostics

```powershell
Enable-Prompt -BlockTimeoutMilliseconds 1500
Enable-StatusLine -BlockTimeoutMilliseconds 1500
[StatusLine.RefreshDiagnostics]::GetSegments()
[StatusLine.RefreshDiagnostics]::GetScriptStatus($project)
[StatusLine.RefreshDiagnostics]::LastStatusLineError
```

Built-in blocks that read files, processes or the network time out after
`-BlockTimeoutMilliseconds` (default 2000). Script definitions use their own
`-TimeoutMilliseconds`. Formatting-only blocks such as Clock and Status never time out.

After a timeout or failure, the last good value is shown in amber with a `[stale]` suffix; a
gradient can override the color, but the suffix remains. A first timeout with no earlier value is
omitted. The next successful refresh clears the marker. `GetScriptStatus` reports a definition's
last refresh time, error and stale state, and `GetSegments` lists the built-in caches.

Timed-out subprocesses and PowerShell pipelines are stopped. Four background workers limit
resource use, so new work can also time out while waiting for a free worker.

## Runtime changes

Edit an enabled surface without replacing its hooks or worker:

```powershell
[StatusLine.Prompt]::Settings.Symbol = '> '
[StatusLine.Prompt]::Settings.Left = 'Directory', 'Git', 'Errors'
[StatusLine.StatusBar]::Settings.IntervalMilliseconds = 500
```

`Settings` validates each assignment and applies it; an invalid edit leaves the active settings
intact. Arrays returned from a getter are copies, so assign the modified array back. After
disabling, the getters return `$null` and setters are rejected. Settings that change installed
hooks or output (prompt shell integration, terminal progress and shell context; status line
target and output path) are read-only here; re-run the Enable command instead.

The helper methods offer the same changes in one call:

~~~powershell
[StatusLine.Prompt]::Configuration
[StatusLine.StatusBar]::Configuration

[StatusLine.Prompt]::SetColors('#1034A6', '#FFAA33')
[StatusLine.Prompt]::AddBlock('Right', $duration)
[StatusLine.StatusBar]::Configure(@{ IntervalMilliseconds = 500; BlendBackground = $true })
~~~

Both classes expose SetColors, SetBlocks, AddBlock, RemoveBlock and Configure. `Configuration`
returns a detached snapshot; selected block definitions keep their identity. Prompt changes apply
on the next prompt; status line changes apply immediately without restarting the worker.

## Shell Integration and Capabilities

```powershell
Enable-Prompt -ShellIntegration Osc133   # Auto (default), Osc133, Osc633, ITerm2 or None
Enable-Prompt -TerminalProgress
[StatusLine.TerminalControl]::Detect()
```

Shell integration marks prompts, input and command output so the terminal can jump between
commands and select their output. `Auto` picks a protocol from the terminal environment and
avoids duplicating VS Code's own integration. Do not run two competing prompt integrations.

The working directory is reported separately: OSC 7 for Contour, WezTerm, kitty and mintty,
OSC 9;9 for Windows Terminal, OSC 633 for VS Code and OSC 1337 for iTerm2. Unknown terminals with
an explicit `Osc133` get command marks only.

`-TerminalProgress` reports a running command as indeterminate OSC 9;4 progress, shown by
terminals such as Windows Terminal, and clears it at the next prompt. It is off by default.

### Contour

With Contour, the prompt also turns DEC line reflow off for the prompt and back on for command
output, and sends OSC 3008 shell and command contexts (host, user, process ID, directory, command
text and exit status). Use `-ShellContext:$false` to omit the contexts. Contour's
`CopyLastCommandPrompt`, `CopyLastCommandOutput` and `CopyLastCommandBlock` actions use these marks.
See [line reflow](https://contour-terminal.org/vt-extensions/line-reflow-mode/) and
[OSC 3008 contexts](https://contour-terminal.org/vt-extensions/osc-3008-context/).

### Capability queries

```powershell
[StatusLine.TerminalControl]::StatusLineQuery
# Send it and read the reply with your own input-safe transport, then:
[StatusLine.TerminalControl]::AcceptResponse("`eP1`$r1`$~`e\")
```

The module never reads keyboard input to probe the terminal. `QueryStatusLine()` sends the query
without waiting for a reply. Without a reply, environment detection applies; a negative reply
turns off status line support for that terminal. Multiplexers may filter these sequences.
`[StatusLine.TerminalControl]::SetTitle('My shell')` sets the window title.

## File Output and Contour PROGRAM

```powershell
$statusLineOptions = @{
    Target = 'File'
    OutputPath = "$env:TEMP/statusline.txt"
    Left = 'Directory', 'Git'
    Right = 'ProcessMemory', 'Clock'
}
Enable-StatusLine @statusLineOptions
```

The file contains one SGR-styled line with space-based group positioning, replaced atomically when it
changes and emptied on stop. It does not write to the shell terminal. The module does not parse
contour.yml: OutputPath must match the path in the profile's PROGRAM command.

```yaml
profiles:
  main:
    status_line:
      display: indicator
      indicator:
        left: '{Command:Program=type "C:\Users\<username>\AppData\Local\Temp\statusline.txt",Interval=1}'
        middle: ""
        right: ""
```

On Unix use `cat /absolute/path/statusline.txt`. Contour runs the command in the background every
`Interval` seconds (whole seconds, default 10, so set `Interval=1`) and shows the previous output until
the next run finishes; the item is empty until the first run completes. A run still going after 5 seconds
(or its interval, if shorter) is killed. Only the first line of standard output is used. The attribute
form splits on commas, so the path must not contain one. The display therefore lags the file by up to
one interval, independent of the module's refresh timer. Choose this mode instead of DEC host-writable
output; the targets cannot be combined. The default filename includes process/runspace identifiers;
explicit file paths must be unique between independent sessions.
See Contour's [indicator status line documentation](https://github.com/contour-terminal/contour/blob/master/docs/configuration/indicator-statusline.md).

## Help and Support

```powershell
Get-Help Enable-Prompt -Examples
Get-Help Enable-StatusLine -Full
Get-Command -Module StatusLine
```

StatusLine runs on Windows, Linux and macOS. See the [command index](en-US/StatusLine.md) and
[license](../LICENSE).
