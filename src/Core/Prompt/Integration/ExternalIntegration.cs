namespace StatusLine;

[Flags]
internal enum ExternalIntegration {
    None = 0, PromptStart = 1, InputStart = 2, OutputStart = 4,
    OutputEnd = 8, WorkingDirectory = 16, Reflow = 32, All = 63,
}
