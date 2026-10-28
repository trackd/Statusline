---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-09-2026
PlatyPS schema version: 2024-05-01
title: Enable-StatusLine
---

# Enable-StatusLine

## SYNOPSIS


Refreshes the status line on a configurable interval with automatic idle pausing.

## SYNTAX

### __AllParameterSets

```
Enable-StatusLine [[-Block] <Object[]>] [-Left <Object[]>] [-Center <Object[]>] [-Right <Object[]>]
 [-StartColor <Color>] [-EndColor <Color>] [-IntervalMilliseconds <int>] [-IdleTimeoutSeconds <int>]
 [-BlockTimeoutMilliseconds <int>] [-UpdateDuringCommand] [-TextContrast <TextContrastMode>]
 [-Style <SegmentStyle>] [-IconSet <IconSet>] [-BlockSeparator] [-DurationWarning] [-Padding <string>]
 [-Force] [-Target <StatusLineTarget>] [-OutputPath <string>] [-WeatherLocation <string>]
 [-BlendBackground] [-DirectoryDepth <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES


None.

## DESCRIPTION


Starts a dedicated background thread that rebuilds the requested blocks every IntervalMilliseconds and
rewrites the status line only when its content changed. Prompt and ReadLine boundary wrappers capture
immutable session snapshots on the calling runspace thread. They preserve existing key bindings and
restore functions only while still owned by this module. Terminal updates pause during command execution
unless UpdateDuringCommand is selected; provider collection and file output continue.
Interactive host frames are committed on the foreground runspace at PowerShell.OnIdle, after the reader establishes its prompt anchor.
While the input buffer contains text, the status bar keeps its latest displayed frame; providers and file output continue.

Script definitions refresh only when their objects are selected in Block or an alignment group. Slow work (git status, weather,
script providers) runs off the render thread and the latest result is shown until it finishes.

Calling Enable-StatusLine again replaces the running configuration. After IdleTimeoutSeconds without shell activity, rendering and new provider refreshes pause until the next accepted input or prompt. The center group stays centered independently of side-group widths. Side groups are fitted to the space left around it with at least a one-cell gap: Directory text is shortened with a middle ellipsis (to a minimum of 12 cells) first, then whole blocks are dropped from the end of the group. Without a center group, right-hand blocks are dropped before left-hand blocks.

Host-writable mode (DECSSDT 2) is selected once at startup, not on every redraw. Support is detected
through [StatusLine.TerminalControl]::Detect() rather than restricted to Contour. Explicit query replies can
override the conservative environment fallback; Force remains available. The timer does not read stdin.
If Enable-Prompt is active, its captured snapshot supplies status and duration along with
location. Slow providers and script definitions share caches across both consumers.

Each refresh can go to the DEC host-writable status line (escape sequences, usable by any terminal that
implements DECSSDT/DECSASD), or to a file.
Choose exactly one target. The file holds one line of text with SGR colors and no
cursor movement, which Contour's indicator status line can show through its `{Command:Program=...}`
placeholder.

Left, Center, and Right override placement. Explicit Block merges with them; groups alone suppress default kinds. Gradient steps use produced blocks. SystemMemory is OS physical usage; ProcessMemory is process working set. Contour history.auto_scroll_on_update must be false to preserve scrollback during background output, or use Disable-StatusLine and Enable-StatusLine. GUI viewport state is not observable through this PTY.

Filled styles blend backgrounds through gaps only when BlendBackground is
selected and the blocks use compatible RGB colors without explicit definition color overrides.
Otherwise segments retain separate fills. Group placement uses explicit spaces so changing side
widths do not move the center. Plain styles keep gaps unfilled; Contour gaps compensate for its
reversed default status-line colors.

## EXAMPLES


### Example 1: Default live status line

```powershell
Enable-StatusLine
```

Shows Cpu, ProcessMemory and Clock as powerline segments with Nerd Font icons, refreshed every second.

### Example 2: System monitor without a Nerd Font

```powershell
$statusLineOptions = @{
    Block = 'Directory', 'Git', 'Cpu', 'Network', 'Battery', 'Weather', 'Clock'
    IntervalMilliseconds = 500
    Style = 'Plain'
    IconSet = 'Unicode'
    BlockSeparator = $true
}
Enable-StatusLine @statusLineOptions
```

Refreshes twice per second using standard Unicode symbols.

### Example 3: Custom block in a chosen position

```powershell
$blockOptions = @{
    Name = 'k8s'
    IntervalMilliseconds = 10000
    Icon = [char]::ConvertFromUtf32(0xF10FE)
    Foreground = '#326CE5'
    ScriptBlock = { kubectl config current-context }
}
$k8s = New-StatusLineBlock @blockOptions
Enable-StatusLine -Block @('Directory', 'Git', $k8s, 'Clock')
```

The definition object is refreshed every ten seconds in the background. Script definitions default to
Right alignment, so it is drawn in the right group before Clock.
Its Name is descriptive, not a registered lookup key.

### Example 4: Feed Contour's indicator status line

```powershell
Enable-StatusLine -Target File -OutputPath ~/.cache/contour-statusline.txt -Verbose
```

Writes each refresh to the file instead of the terminal; Verbose prints the matching profile setting.
Missing parent directories are created.
Reference it from the Contour profile (on Windows use `type \"C:\\path\\file.txt\"`):

```yaml
profiles:
  main:
    status_line:
      display: indicator
      indicator:
        left: "{Command:Program=cat /home/me/.cache/contour-statusline.txt,Interval=1}"
        middle: ""
        right: ""
```

### Example 5: Explicit groups with gradient

```powershell
$statusLineOptions = @{
    Left = 'Directory', 'Git'
    Center = 'PowerShell'
    Right = 'SystemMemory', 'ProcessMemory', 'Clock'
    StartColor = '#275DAD'
    EndColor = '#E8A33D'
}
Enable-StatusLine @statusLineOptions
```

Each group keeps its input order; colors are stepped over actual produced blocks.

### Example 6: Stop updates while reading scrollback

```powershell
Disable-StatusLine
Enable-StatusLine
```

Stop automatic output, then enable it again. Reuse your saved parameters when enabling to restore custom settings. Disable also clears file output; definition objects remain reusable.

## PARAMETERS

### -Block

Built-in names, reusable definitions, or static styled blocks in their natural alignment. Group selections
override natural alignment; Block retains each block's alignment. Supplying only Block suppresses
the default Block selection. Pass definition objects rather than their descriptive names.

```yaml
Type: System.Object[]
DefaultValue: 'Cpu ProcessMemory Clock'
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -TextContrast

Foreground selection for filled segments: Automatic (default), Preserve, Black, or White.
Automatic selects contrasting black/white for RGB fills and retains supplied/default text colors
for terminal palette fills. Explicit definition foreground colors retain precedence.

```yaml
Type: StatusLine.TextContrastMode
DefaultValue: Automatic
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: [Automatic, Preserve, Black, White]
HelpMessage: ''
```

### -DurationWarning

Append a red warning icon after Duration when the last invocation failed. Disabled by default.
Retained errors from earlier commands do not trigger this warning after a successful invocation.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: false
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```


### -Padding

Text inside both edges of a block: Padding + icon/text content + Padding. The icon and text use one ordinary space. Defaults to a space; accepts empty strings and thin spaces.

```yaml
Type: System.String
DefaultValue: "' '"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```



### -DirectoryDepth

Maximum trailing components shown in relative directory paths. The default is 3; zero disables shortening.
The value is shared by prompt and statusline. When omitted, the value set by an earlier call is kept.

```yaml
Type: System.Int32
DefaultValue: 3
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
```


### -BlockTimeoutMilliseconds

Maximum time for each I/O-backed built-in block refresh, including queue time. Range 50-60000; default 2000. Timed-out values retain their last successful data with a [stale] marker. Pure formatting blocks do not schedule background work. Script definitions use their own TimeoutMilliseconds.

```yaml
Type: System.Int32
DefaultValue: "2000"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Center

Ordered kind names forced into this alignment group. Built-in and block definition objects are accepted. Providing any group suppresses the default Block list; explicitly supplied Block is merged first, followed by Left, Center, Right. Omitted conditional blocks do not consume gradient steps.

```yaml
Type: System.Object[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -EndColor

Gradient endpoint. Supply both StartColor and EndColor or neither. Accepts a color name, #RRGGBB, or Drawing.Color. Steps span the actual produced block count: zero blocks have no ramp, one uses StartColor, multiple include both endpoints. Plain uses foregrounds; filled styles use backgrounds and contrasting text. A requested gradient overrides built-in accents; explicit definition colors retain precedence.

```yaml
Type: System.Drawing.Color
DefaultValue: "Color [Empty]"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Force

Starts even when host-writable status-line support is not detected or output is redirected.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "False"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -IconSet

Glyphs for built-in block icons. NerdFont uses Nerd Font icons, Unicode uses standard symbols and emoji, None uses short ASCII labels such as RAM and CPU.

```yaml
Type: StatusLine.IconSet
DefaultValue: "NerdFont"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: ["NerdFont", "Unicode", "None"]
HelpMessage: ''
```

### -IdleTimeoutSeconds

Seconds without shell activity before background rendering, provider scheduling, and file updates pause. Default: 60; zero disables idle pausing. Existing bounded provider calls may finish. Command acceptance (including blank input), prompt readiness and reader startup resume updates. The worker checks the deadline independently of PowerShell.OnIdle, including with unfinished input, and PowerShell.Exiting cancels the worker. Idle detection measures shell activity, not terminal visibility or individual keystrokes.

```yaml
Type: System.Int32
DefaultValue: "60"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -IntervalMilliseconds

Time between refreshes. Range 100-60000.

```yaml
Type: System.Int32
DefaultValue: "1000"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Left

Ordered kind names forced into this alignment group. Built-in and block definition objects are accepted. Providing any group suppresses the default Block list; explicitly supplied Block is merged first, followed by Left, Center, Right. Omitted conditional blocks do not consume gradient steps.

```yaml
Type: System.Object[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -OutputPath

FileSystem path written when Target is File. Other PowerShell providers are rejected before creating
output or starting a worker. It is replaced atomically on every change and emptied when the
updater stops. The default path contains the process and runspace identifiers. Use an explicit path matching the Contour PROGRAM configuration.

```yaml
Type: System.String
DefaultValue: "<temporary directory>/statusline-<processId>-<runspaceId>.txt"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Right

Ordered kind names forced into this alignment group. Built-in and block definition objects are accepted. Providing any group suppresses the default Block list; explicitly supplied Block is merged first, followed by Left, Center, Right. Omitted conditional blocks do not consume gradient steps.

```yaml
Type: System.Object[]
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -BlockSeparator

Draw transparent breaks between filled Powerline blocks. Other styles ignore it. Disabled by default.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: false
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -StartColor

Gradient endpoint. Supply both StartColor and EndColor or neither. Accepts a color name, #RRGGBB, or Drawing.Color. Steps span the actual produced block count: zero blocks have no ramp, one uses StartColor, multiple include both endpoints. Plain uses foregrounds; filled styles use backgrounds and contrasting text. A requested gradient overrides built-in accents; explicit definition colors retain precedence.

```yaml
Type: System.Drawing.Color
DefaultValue: "Color [Empty]"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Style

Plain draws colored text. Powerline joins filled side groups with arrows and gives center blocks rounded caps. Diamond draws each block as a separate rounded pill with a one-cell gap. Rounded joins blocks like Powerline but with rounded separators. BlockSeparator applies only to Powerline. Filled styles require a font with the corresponding Powerline/Nerd Font glyphs.

```yaml
Type: StatusLine.SegmentStyle
DefaultValue: "Powerline"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: ["Plain", "Powerline", "Diamond", "Rounded"]
HelpMessage: ''
```

### -Target

Where each refresh goes. HostWritable draws the DEC host-writable status line with escape sequences.
File writes a single line to OutputPath for Contour's indicator `{Command:Program=...}` placeholder.
The unfinished Contour extension is a separate prototype, not a public target. Combined targets are rejected.

```yaml
Type: StatusLine.StatusLineTarget
DefaultValue: "HostWritable"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: ["HostWritable", "File"]
HelpMessage: ''
```

### -UpdateDuringCommand

Opt in to host-writable refreshes while commands execute. By default terminal updates pause between ReadLine acceptance and the next prompt; provider collection and file output continue. Continuous terminal writes can interfere with foreground terminal applications.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "False"
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -WeatherLocation

Location passed to wttr.in, for example Stockholm, Sweden. Blank uses best-effort IP location.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```


### -BlendBackground

Opt in to RGB gradients between filled segments and across status-line gaps. Omit for separate boxes. Terminal-palette fills are not interpolated.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```


## INPUTS

## OUTPUTS


### None

This cmdlet writes a warning and does nothing when host-writable status-line support is not detected or output is redirected, unless Force is specified.

## NOTES


HostWritable requires `permissions.display_host_writable_statusline: allow` in the Contour configuration.
Each update draws the host-writable line and selects the main display
again. Contour maintains separate cursor state for the status display.
Contour runs the `{Command}` program in the background every `Interval` seconds (whole seconds,
default 10; use `Interval=1`) and keeps only the first line of standard output. It shows the previous
output until a run completes, so the indicator lags the file by up to one interval and is empty before
the first run. A run still going after 5 seconds, or after its interval if that is shorter, is killed.
The attribute form splits on commas, so OutputPath must not contain one. The file is shared by every
session writing to it; the last writer wins.
Without our prompt enabled, Status reflects $LASTEXITCODE (native commands), and Duration is unavailable
to the timer. With Enable-Prompt, cmdlet failure status and history duration are shared with it.
For continuous scrollback-safe refreshes set profiles.main.history.auto_scroll_on_update: false in your existing Contour profile. This also affects ordinary output; the module does not modify terminal config.

## RELATED LINKS


- [Disable-StatusLine](Disable-StatusLine.md)
- [Enable-Prompt](Enable-Prompt.md)
- [Disable-Prompt](Disable-Prompt.md)
- [New-StatusLineBlock](New-StatusLineBlock.md)
