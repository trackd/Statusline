---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-23-2026
PlatyPS schema version: 2024-05-01
title: Show-StatusLine
---

# Show-StatusLine

## SYNOPSIS

Previews a status row without enabling it.

## SYNTAX

### __AllParameterSets

```
Show-StatusLine [[-Configuration] <StatusBarConfiguration>] [-Width <int>] [-ExitCode <int>]
 [-Duration <timespan>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Returns a styled status row preview as a string without installing hooks, starting the statusline updater, or selecting a terminal display. Due refreshes for script blocks and slow providers start together, and the preview waits up to the configured BlockTimeoutMilliseconds for them; values that are still not ready are omitted. Script output is cached for the block's IntervalMilliseconds, so an immediately repeated preview with different -ExitCode or -Duration values can show the earlier result.

## EXAMPLES

### Example 1

```powershell
Show-StatusLine -Width 80
```

Renders the default status line (Cpu, ProcessMemory, Clock) at 80 columns. Pass
`[StatusLine.StatusBar]::Configuration` to preview the status line running in the current runspace.

### Example 2: Preview after a failed command

```powershell
$settings = [StatusLine.StatusBarConfiguration]@{ Left = 'Status', 'Duration'; Right = 'Clock' }
Show-StatusLine $settings -Width 120 -ExitCode 1 -Duration ([timespan]::FromSeconds(3))
```

Previews status and duration blocks at 120 columns as they would look after a failing three-second command.

## PARAMETERS

### -Configuration

Typed settings to preview. Defaults match the corresponding Enable command's defaults, not the currently enabled configuration. Settings are validated before rendering.

```yaml
Type: StatusLine.StatusBarConfiguration
DefaultValue: StatusBarConfiguration with Block Cpu, ProcessMemory, Clock
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

### -Duration

Optional last-command duration for a deterministic Duration block preview.

```yaml
Type: System.Nullable`1[System.TimeSpan]
DefaultValue: None
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

### -ExitCode

Optional last-command exit code for Status and Duration blocks.

```yaml
Type: System.Nullable`1[System.Int32]
DefaultValue: None
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

### -Width

Available terminal columns. Default 120; maximum 32767. Minimum 2 for Show-Prompt and 1 for Show-StatusLine.

```yaml
Type: System.Int32
DefaultValue: 120
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

None.

## OUTPUTS

### System.String

Returns a styled status row preview as a string without installing hooks, starting the statusline updater, or selecting a terminal display. Due refreshes for script blocks and slow providers start together, and the preview waits up to the configured BlockTimeoutMilliseconds for them; values that are still not ready are omitted. Script output is cached for the block's IntervalMilliseconds, so an immediately repeated preview with different -ExitCode or -Duration values can show the earlier result.

## NOTES

Preview strings contain ANSI styling. Redirected output may suppress its display. Custom block scripts may execute asynchronously; previewing is not a sandbox for user scripts.

## RELATED LINKS

[StatusLine Module](StatusLine.md)
