---
document type: cmdlet
external help file: StatusLine.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: StatusLine
ms.date: 10-09-2026
PlatyPS schema version: 2024-05-01
title: Disable-Prompt
---

# Disable-Prompt

## SYNOPSIS


Restores the previous prompt and owned ReadLine wrapper/settings.

## SYNTAX

### __AllParameterSets

```
Disable-Prompt [<CommonParameters>]
```

## ALIASES


None.

## DESCRIPTION


Restores the prior prompt and PSConsoleHostReadLine while still owned. Restores PSReadLine PromptText
only if the prompt and the temporary PromptText value are still owned. ExtraPromptLineCount and
key handlers are not changed. Does not stop a separately running status-line timer.
Expected terminal cleanup I/O failures do not prevent restoration; inspect
`[StatusLine.PromptSession]::LastTerminalError` for the latest optional write failure.

## EXAMPLES


### Example 1

```powershell
Disable-Prompt
```

Restores the previous prompt and owned ReadLine wrapper/settings.

### Example 2: Switch to a different layout

```powershell
Disable-Prompt
Enable-Prompt -Left Directory, Git -Right Clock
```

Restores the original prompt, then installs a new layout.

## PARAMETERS

## INPUTS

## OUTPUTS


### None

This cmdlet produces no output.

## NOTES


Safe to call repeatedly. Removing the module also disables its prompt.

## RELATED LINKS


- [Enable-StatusLine](Enable-StatusLine.md)
- [Disable-StatusLine](Disable-StatusLine.md)
- [Enable-Prompt](Enable-Prompt.md)
- [New-StatusLineBlock](New-StatusLineBlock.md)
