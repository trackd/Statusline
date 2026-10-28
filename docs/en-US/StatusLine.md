---
document type: module
Help Version: 0.3.0
HelpInfoUri: ''
Locale: en-US
Module Guid: 5ee48f8d-7f90-4f03-83d9-569c92cb56fa
Module Name: StatusLine
ms.date: 10-09-2026
PlatyPS schema version: 2024-05-01
title: StatusLine Module
---

# StatusLine Module

## Description

Shared styled blocks for PowerShell prompts and DEC host-writable status lines.
Eight cmdlets cover setup, previews, reusable block definitions, and Unicode measurement.
Enable/Disable install or stop each surface; New-StatusLineBlock creates a definition to select
by object. Blocks work in both prompts and status lines. No registration is required.
Importing the module does not install prompt hooks or start background updates.

## Cmdlets

### [Enable-StatusLine](Enable-StatusLine.md)

Refreshes the status line on a configurable interval with automatic idle pausing.

### [Disable-StatusLine](Disable-StatusLine.md)

Stops the background status line updater.

### [Enable-Prompt](Enable-Prompt.md)

Installs a configurable prompt using shared terminal blocks.

### [Disable-Prompt](Disable-Prompt.md)

Restores the previous prompt and owned ReadLine wrapper/settings.

### [New-StatusLineBlock](New-StatusLineBlock.md)

Creates a reusable built-in or script-backed definition without executing it.


### [Measure-Codepoint](Measure-Codepoint.md)

Measures grapheme count and terminal columns, skipping terminal escape sequences.

### [Show-Prompt](Show-Prompt.md) / [Show-StatusLine](Show-StatusLine.md)

Preview a configuration without installing it.

## Advanced helpers

`[StatusLine.PromptPreview]::Render($configuration)` returns a preview without installing hooks.
`[StatusLine.TerminalControl]::Detect()` reports capabilities; `StatusLineQuery` returns the query
sequence; `QueryStatusLine()` writes it; `AcceptResponse($reply)` validates a caller-supplied reply.
`SetTitle($text)` updates the window title, and `TitleSequence($text)` returns its VT sequence.
`DetectProcess()` returns cached Windows terminal ancestor metadata when identifiable.
`[StatusLine.RefreshDiagnostics]::GetSegments()` reports cache state; `LastStatusLineError` reports updater errors.
`[StatusLine.BlockSnapshots]::Create()` creates styled blocks; `Snapshot($options)` collects built-in/script definitions.
`TerminalControl` exposes TextSequence, BlocksSequence, ModeSequence, WriteText, WriteBlocks, and SetMode for one-shot output.
See the [configuration guide](../Configuration.md) for full configuration, runtime changes and provider timeouts.
