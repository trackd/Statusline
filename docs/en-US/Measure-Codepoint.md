---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-23-2026
PlatyPS schema version: 2024-05-01
title: Measure-Codepoint
---

# Measure-Codepoint

## SYNOPSIS

Measures grapheme count and terminal columns in a string.

## SYNTAX

### __AllParameterSets

```
Measure-Codepoint [-InputString] <string> [-IncludeEscSequences] [<CommonParameters>]
```

## ALIASES

None.

## DESCRIPTION

Uses the Unicode table implementation to report the UTF-16 offset consumed, grapheme cursor
movements, terminal columns, and whether terminal escape sequences were encountered.
Escape sequences are skipped unless IncludeEscSequences is supplied. This cmdlet uses Unicode measurement independently of the
experimental TextMeasurement.UseUnicode renderer setting.

## EXAMPLES

### Example 1: Measure a string

```powershell
'日本' | Measure-Codepoint
```

Returns two grapheme cursor movements and four terminal columns.

### Example 2: Measure styled text

```powershell
Measure-Codepoint "`e[31mred`e[0m"
```

Returns three columns and ContainsVT `True`; escape sequences occupy no cells. Add `-IncludeEscSequences` to count them as printable characters (12 columns).

## PARAMETERS

### -InputString

String to measure. Accepts an empty string, direct pipeline input, or an InputString property.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -IncludeEscSequences

Treat escape characters as input to measure rather than skipping terminal control sequences.
This reports inspection width, not the visible width of styled terminal text.

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

### System.String

## OUTPUTS

### StatusLine.Unicode+MeasurementResult

Offset, CursorMovements, Columns, and ContainsVT.

## NOTES

IncludeEscSequences measures the escape characters rather than recognizing them as terminal
controls. It is an inspection option, not the visible width of styled terminal text.

## RELATED LINKS

[StatusLine Module](StatusLine.md)
