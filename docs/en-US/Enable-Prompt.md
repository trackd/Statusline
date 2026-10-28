---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-09-2026
PlatyPS schema version: 2024-05-01
title: Enable-Prompt
---

# Enable-Prompt

## SYNOPSIS


Installs a configurable prompt using shared terminal blocks.

## SYNTAX

### __AllParameterSets

```
Enable-Prompt [-Configuration <PromptConfiguration>] [-Left <Object[]>] [-Right <Object[]>]
 [-Block <Object[]>] [-Input <Object[]>] [-TextContrast <TextContrastMode>] [-Style <SegmentStyle>]
 [-IconSet <IconSet>] [-ShellIntegration <ShellIntegration>] [-TerminalProgress] [-Transient]
 [-TransientBlock <Object[]>] [-ConnectRows] [-Rows <int>] [-BlockTimeoutMilliseconds <int>]
 [-StartColor <Color>] [-EndColor <Color>] [-ElevatedColorStart <Color>] [-ElevatedColorEnd <Color>]
 [-WeatherLocation <string>] [-DirectoryDepth <int>] [-BlockSeparator] [-DurationWarning]
 [-ShellContext] [-Padding <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES


None.

## DESCRIPTION


Installs an explicitly enabled shared-block prompt. Selecting right-hand or input-row blocks forces
two rows: context above editable input. Otherwise Rows selects one or two rows. Transient
orchestration wraps PSConsoleHostReadLine and redraws after the original reader returns, including
blank input. Key bindings and ExtraPromptLineCount remain unchanged. Installation temporarily
sets PSReadLine PromptText to an empty suffix; disable restores it while still owned.
Command facts and provider caches are shared with the independently running status-line timer.
Null and invalid configurations are rejected. Failed configuration does not change shared directory
depth. Optional terminal-marker write failures do not discard accepted input; the last failure is
available through `[StatusLine.PromptSession]::LastTerminalError`.

Transient text inherits StartColor when a gradient is configured, otherwise the first segment accent of the last full prompt (built-in blue before the first full render). Its background stays unfilled and the style resets afterward. No providers run during transient rendering.

Rows are fitted to the terminal width. Directory text is shortened with a middle ellipsis (to a
minimum of 12 cells) before whole blocks are dropped from the end of the row, right-hand blocks
first. Left and right blocks keep at least a one-cell gap. A one-row prompt reserves
min(20, half the width) cells for typing, so a crowded prompt does not push input onto the next line.

## EXAMPLES


### Example 1: Right context and gradient

```powershell
Enable-Prompt -Left Directory,Git -Right Duration,Clock -StartColor '#275DAD' -EndColor '#E8A33D' -Transient
```

Use a context row with gradient segment fills and transient history, above editable input.

### Example 2: Project and runtime context

```powershell
Enable-Prompt -Left Directory,Git,Node -Right Status,Clock
```

Left and right blocks share the context row, above editable input.

### Example 3: Right context row

```powershell
Enable-Prompt -Right SystemMemory,ProcessMemory,Clock
```

Move right blocks to the padded upper context row.

### Example 4: Reusable settings

```powershell
$settings = [StatusLine.PromptConfiguration]@{
    Rows = 2
    Symbol = '$ '
    TransientSymbol = '$ '
    StartColor = '#244A73'
    EndColor = '#62C4A6'
    ShellIntegration = 'None'
}
Enable-Prompt -Configuration $settings -Transient
```

Use typed configuration for additional symbol settings.

## PARAMETERS

### -TextContrast

Foreground selection for filled segments: Automatic (default), Preserve, Black, or White.
Automatic selects contrasting black/white for RGB fills and retains supplied/default text colors
for terminal palette fills. Explicit definition foreground colors retain precedence.

```yaml
Type: StatusLine.TextContrastMode
DefaultValue: Configuration.TextContrast
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
```



### -TerminalProgress

Opt in to OSC 9;4 command progress in supporting terminals. For example:
`Enable-Prompt -TerminalProgress`.

Emits indeterminate busy (`ESC ] 9;4;3;50 ST`) after accepting a nonblank command,
before the output-start marker. Clears it (`ESC ] 9;4;0;50 ST`) after the
output-end marker, before rendering the next prompt. Transient redraws do not
change progress. Also works with `-ShellIntegration None`. Disabled by default;
use `-TerminalProgress:$false` to override an enabled configuration.

See the [OSC 9;4 specification](https://contour-terminal.org/vt-extensions/osc-9-4-progress/).

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "Configuration.TerminalProgress (false)"
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

### -ShellContext

Emit OSC 3008 shell and command metadata in Contour alongside OSC 133. Enabled by default; use -ShellContext:$false to disable.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "Configuration.ShellContext (true)"
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
DefaultValue: "Configuration.Padding"
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


### -Block

Additional blocks that retain their own alignment: built-in names, block definitions, or static
blocks created by [StatusLine.BlockSnapshots]::Create. Without -Configuration, supplying Block
alone clears the default Left selection.

```yaml
Type: System.Object[]
DefaultValue: "Configuration.Block"
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

### -Input

Shared built-in names or reusable block kinds on the lower input row, before Symbol. Selecting input kinds forces two rows. An empty array clears the selection.

```yaml
Type: System.Object[]
DefaultValue: "Configuration.Input"
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

### -BlockTimeoutMilliseconds

Maximum time for each I/O-backed built-in block refresh, including queue time. Range 50-60000; default 2000. Overrides Configuration.BlockTimeoutMilliseconds. Prompt rendering never waits for these refreshes. Script definitions use their own TimeoutMilliseconds.

```yaml
Type: System.Int32
DefaultValue: "Configuration.BlockTimeoutMilliseconds"
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

### -Configuration

Reusable PromptConfiguration. Defaults: Left=Directory/Git/Status/Duration, Right empty,
Rows=1 (right-hand or input-row blocks force two rows), Style=Powerline, IconSet=NerdFont,
Padding=" ", ShellIntegration=Auto, ShellContext=true, TerminalProgress=false, Transient=false,
BlockTimeoutMilliseconds=2000.

With ConnectRows, ContextSymbol ("╭─ ") starts the upper row and InputLinePrefix ("╰─ ") starts
the lower row. Symbol appears immediately before editable input; TransientSymbol replaces the full
prompt in history. On a two-row prompt without visible input-row blocks, a blank Symbol falls back
to TransientSymbol. Set both symbols empty to hide the input marker.
Input adds reusable content to the lower row before Symbol.

Padding is text on both sides inside each block. Icons and labels use one ordinary space. BlockSeparator adds transparent
Powerline breaks between filled blocks; it does not insert literal spaces.

```yaml
Type: StatusLine.PromptConfiguration
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
DefaultValue: "Configuration.EndColor"
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

### -ElevatedColorStart

Gradient endpoint for elevated sessions. Supply both ElevatedColorStart and ElevatedColorEnd or neither. Accepts a color name, hex string, or Drawing.Color. Overrides the normal gradient throughout the prompt, including input-row blocks and the transient accent, without requiring the Elevation kind. Unelevated sessions retain StartColor/EndColor. Omit both to use normal colors in all sessions.

```yaml
Type: System.Drawing.Color
DefaultValue: "Configuration.ElevatedColorStart"
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

### -ElevatedColorEnd

Gradient endpoint for elevated sessions. Supply both ElevatedColorStart and ElevatedColorEnd or neither. Accepts a color name, hex string, or Drawing.Color. Overrides the normal gradient throughout the prompt, including input-row blocks and the transient accent, without requiring the Elevation kind. Unelevated sessions retain StartColor/EndColor. Omit both to use normal colors in all sessions.

```yaml
Type: System.Drawing.Color
DefaultValue: "Configuration.ElevatedColorEnd"
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

Override icons: NerdFont, Unicode, or None. NerdFont requires a patched font.

```yaml
Type: StatusLine.IconSet
DefaultValue: "Configuration.IconSet"
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

### -Left

Override the ordered prompt group. Built-in and block definition objects are supported; group placement overrides natural alignment. Without an explicit Configuration, providing one group (or Block) leaves the other empty. With Configuration, omitted groups retain its selections. Empty arrays clear a group.

```yaml
Type: System.Object[]
DefaultValue: "Configuration.Left"
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

Override the ordered prompt group. Built-in and block definition objects are supported; group placement overrides natural alignment. Without an explicit Configuration, providing one group (or Block) leaves the other empty. With Configuration, omitted groups retain its selections. Empty arrays clear a group.

```yaml
Type: System.Object[]
DefaultValue: "Configuration.Right"
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

### -TransientBlock

Optional built-in kind names or block definition objects to display before TransientSymbol.
A nonempty selection enables transient replacement unless -Transient:$false is explicit. Values are cached when the normal prompt is
drawn and reused for its transient replacement, without refreshing providers. For example:
Enable-Prompt -TransientBlock Duration. An empty selection keeps symbol-only output.

```yaml
Type: System.Object[]
DefaultValue: "Configuration.TransientBlock"
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

### -ConnectRows

Show the configured upper and lower row markers on multiline prompts (╭─ and ╰─).
Omitted by default. Use -ConnectRows:$false to override a configuration that enables them.
This does not change the selected row count.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "Configuration.ConnectRows (false)"
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

### -Rows

Number of prompt rows without right-hand or input-row blocks: 1 (default) or 2.
Selecting right-hand or input-row blocks forces two rows. Otherwise Rows controls the layout.

```yaml
Type: System.Int32
DefaultValue: "Configuration.Rows"
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

### -ShellIntegration

Auto, None, Osc133, Osc633, or ITerm2. Auto uses terminal environment and avoids duplicate VS Code integration when VSCODE_SHELL_INTEGRATION is set. Explicit choices override Auto. Auto selects OSC 133 for Windows Terminal, Contour, WezTerm, Ghostty, kitty, Rio, and foot; OSC 633 (with Cwd properties) for VS Code; ITerm2 (OSC 133 marks with OSC 1337 CurrentDir) for iTerm2; and None elsewhere. Lifecycle emits prompt-start, input-start, command-start, and completion markers. Working-directory reporting is terminal-specific: OSC 9;9 in Windows Terminal, OSC 7 in Contour, WezTerm, Ghostty, kitty, Rio, and foot.

```yaml
Type: StatusLine.ShellIntegration
DefaultValue: "Configuration.ShellIntegration"
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
AcceptedValues: ["Auto", "None", "Osc133", "Osc633", "ITerm2"]
HelpMessage: ''
```

### -StartColor

Gradient endpoint. Supply both StartColor and EndColor or neither. Accepts a color name, #RRGGBB, or Drawing.Color. Steps span the actual produced block count: zero blocks have no ramp, one uses StartColor, multiple include both endpoints. Plain uses foregrounds; filled styles use backgrounds and contrasting text. A requested gradient overrides built-in accents; explicit definition colors retain precedence.

```yaml
Type: System.Drawing.Color
DefaultValue: "Configuration.StartColor"
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
DefaultValue: "Configuration.Style"
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

### -Transient

Compact accepted input, including blank and whitespace-only lines, after the original reader returns.
Acceptance occurs once; custom Enter and validation handlers remain installed. Prompt installation
temporarily changes PSReadLine PromptText as described above. Requires a PSReadLine-backed original
reader. Redirection, screen-reader mode, resizing, an off-screen prompt origin, and input with
uncertain character widths retain the full prompt.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: "Configuration.Transient"
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


## INPUTS

## OUTPUTS


### None

Enable-Prompt does not write pipeline output.

## NOTES


PowerShell 7.6+ on Windows, macOS, and Linux. The original reader must use PSReadLine for transient redraw. Competing prompt integrations should be disabled first. Restore only occurs while the module still owns a function; a later replacement from another tool is preserved.

## RELATED LINKS


- [Enable-StatusLine](Enable-StatusLine.md)
- [Disable-StatusLine](Disable-StatusLine.md)
- [Disable-Prompt](Disable-Prompt.md)
- [New-StatusLineBlock](New-StatusLineBlock.md)
