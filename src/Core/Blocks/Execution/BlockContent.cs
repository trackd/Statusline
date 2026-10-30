namespace StatusLine.Blocks.Execution;

internal readonly record struct BlockContent(string Icon, string Text,
    StatusLineAlignment Alignment, TerminalColor Accent) {
    public static implicit operator BlockContent(
        (string Icon, string Text, StatusLineAlignment Alignment, TerminalColor Accent) value) =>
        new(value.Icon, value.Text, value.Alignment, value.Accent);
}
