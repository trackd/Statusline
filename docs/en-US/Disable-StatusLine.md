---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-09-2026
PlatyPS schema version: 2024-05-01
title: Disable-StatusLine
---

# Disable-StatusLine

## SYNOPSIS


Stops the background status line updater.

## SYNTAX

### __AllParameterSets

```
Disable-StatusLine [[-Mode] <StatusLineMode>] [-Force] [<CommonParameters>]
```

## ALIASES


None.

## DESCRIPTION


Stops the thread started by Enable-StatusLine and switches the status line to the given mode.
Idle/exit event subscriptions and owned boundary wrappers are removed, and file output is emptied. Definition objects remain reusable; a separately enabled prompt continues working. It is safe to call when nothing is running.

If another runspace owns the status line, a non-terminating `StatusLineOwnedByAnotherRunspace` error is written and nothing changes. If the worker does not stop within five seconds, a terminating `StatusLineStopTimeout` error is raised and ownership is retained.

## EXAMPLES


### Example 1: Stop and restore the indicator

```powershell
Disable-StatusLine
```

Stops updating and restores Contour's configured indicator line.

### Example 2: Stop and hide the status line

```powershell
Disable-StatusLine -Mode Hidden
```

Stops updating and hides the status line.

## PARAMETERS

### -Force

Writes the mode sequence even when host-writable status-line support is not detected or output is redirected.

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

### -Mode

Status line mode to switch to after stopping. Default: Indicator.

```yaml
Type: StatusLine.StatusLineMode
DefaultValue: "Indicator"
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
AcceptedValues: ["Hidden", "Indicator", "HostWritable"]
HelpMessage: ''
```

## INPUTS

## OUTPUTS


### None

This cmdlet produces no output.

## NOTES


Remove a definition from all prompt/statusline selections to stop new refresh requests.

## RELATED LINKS


- [Enable-StatusLine](Enable-StatusLine.md)
- [Enable-Prompt](Enable-Prompt.md)
- [Disable-Prompt](Disable-Prompt.md)
- [New-StatusLineBlock](New-StatusLineBlock.md)
