namespace StatusLine.Blocks.Execution;

// Capture once for both cache identity and composition. Definition identity alone cannot
// detect direct property edits made by PowerShell or .NET callers.
internal readonly record struct DefinitionAppearance(TerminalColor? Foreground, TerminalColor? Background,
    StatusLineAlignment? Alignment, string? Padding, string? Icon, bool? Bold, bool? Italic, bool? Underline);
