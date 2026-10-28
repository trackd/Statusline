---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-19-2026
PlatyPS schema version: 2024-05-01
title: New-StatusLineBlock
---

# New-StatusLineBlock

## SYNOPSIS

Creates a reusable built-in or script-backed block definition.

## SYNTAX

### Kind (Default)

```
New-StatusLineBlock [-Kind] <StatusLineBlockKind> [-Name <string>] [-IntervalMilliseconds <int>]
 [-TimeoutMilliseconds <int>] [-Icon <string>] [-Foreground <TerminalColor>]
 [-Background <TerminalColor>] [-Alignment <StatusLineAlignment>] [-Bold] [-Italic] [-Underline]
 [-Padding <string>] [<CommonParameters>]
```

### Script

```
New-StatusLineBlock [-ScriptBlock] <scriptblock> [-Name <string>] [-IntervalMilliseconds <int>]
 [-TimeoutMilliseconds <int>] [-Icon <string>] [-Foreground <TerminalColor>]
 [-Background <TerminalColor>] [-Alignment <StatusLineAlignment>] [-Bold] [-Italic] [-Underline]
 [-Padding <string>] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Returns a definition without executing or registering it. Pass the object to the prompt or statusline
Block or kind-selection parameters. A Name is descriptive and cannot be used to look up the object.
The same definition object shares its script provider cache between consumers. Creating another
definition with the same name does not replace the first.

Built-in definitions reuse the normal kind providers and formatting. Script definitions receive a
StatusLineState argument, execute in a shared background runspace pool, and cannot capture caller
variables. Scripts retain their returned objects until display formatting; styled StatusLineBlock
objects are supported. Refresh work starts on demand when the definition is selected. Removing it
from all consumers stops new refresh requests; an in-flight request can finish or time out.

Explicit foreground/background colors override global gradients. Other appearance settings inherit
normal behavior when omitted. Explicit group placement overrides the definition alignment.

## EXAMPLES

### Example 1: Style a built-in block

```powershell
$duration = New-StatusLineBlock -Kind Duration -Foreground White -Background '#1034A6'
Enable-Prompt -Left Directory -Right $duration
```

### Example 2: Share a custom provider

```powershell
$processes = New-StatusLineBlock -Name Processes -ScriptBlock {
    (Get-Process).Count
} -IntervalMilliseconds 5000 -TimeoutMilliseconds 1000

Enable-Prompt -Left @('Directory', $processes)
Enable-StatusLine -Left $processes -Right Clock
```

## PARAMETERS

### -Alignment

Default placement. Explicit Left, Center, Right and input/transient groups override it.

```yaml
Type: StatusLine.StatusLineAlignment
DefaultValue: Kind's natural alignment; Right for script definitions
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
AcceptedValues: [Left, Center, Right]
HelpMessage: ''
```

### -Background

Background color, overriding the global gradient for this definition.

```yaml
Type: StatusLine.TerminalColor
DefaultValue: None (inherits)
SupportsWildcards: false
Aliases:
- BackgroundColor
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

### -Bold

Use bold text.

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

### -Foreground

Text color, as an RGB color/name/hex string or a System.ConsoleColor palette entry.

```yaml
Type: StatusLine.TerminalColor
DefaultValue: None (inherits)
SupportsWildcards: false
Aliases:
- ForegroundColor
- Color
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

### -Icon

Optional icon replacement. An empty string hides the icon.

```yaml
Type: System.String
DefaultValue: None (inherits the kind's icon)
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

Minimum interval between custom script refreshes, 250-86400000 ms. Default 5000.

```yaml
Type: System.Int32
DefaultValue: 5000
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

### -Italic

Use italic text.

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

### -Kind

Built-in provider to reuse. Mutually exclusive with ScriptBlock.

```yaml
Type: StatusLine.StatusLineBlockKind
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Kind
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Name

Optional diagnostic label. It does not register a name.

```yaml
Type: System.String
DefaultValue: Kind name, or Script
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

### -Padding

String on each side of the whole block. When omitted, inherits consumer padding.

```yaml
Type: System.String
DefaultValue: None (inherits consumer padding)
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

### -ScriptBlock

Script evaluated in the background while selected. Its only argument is a StatusLine.StatusLineState snapshot with Location, FileSystemPath, LastExitCode, LastDuration, ErrorCount, StackCount, NestedPromptLevel, RunspaceCount and HistoryId. Each output object becomes a block; anything written to the error stream fails the refresh. See "The $state argument" in docs/Configuration.md for details.

Location, FileSystemPath, LastExitCode, LastDuration,ErrorCount, StackCount, NestedPromptLevel, RunspaceCount, HistoryId
access it through a normal param block on the scriptblock.

```yaml
Type: System.Management.Automation.ScriptBlock
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Script
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -TimeoutMilliseconds

Custom script deadline, 50-60000 ms, including queue time. Default 2000.

```yaml
Type: System.Int32
DefaultValue: 2000
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

### -Underline

Use underlined text.

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

### StatusLine.StatusLineBlockDefinition

A reusable block definition; no provider work starts until selected.

## NOTES

Use the same object across consumers to share its custom cache. Dispose only when no consumer
uses the definition. No unregister step is required.

## RELATED LINKS

- [Enable-Prompt](Enable-Prompt.md)
- [Enable-StatusLine](Enable-StatusLine.md)
