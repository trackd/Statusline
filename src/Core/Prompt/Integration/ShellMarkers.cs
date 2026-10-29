namespace StatusLine;

internal static class ShellMarkers {
    public static string Begin(ShellIntegrationProfile profile, int? exitCode, string? path, bool finish = false, bool terminalProgress = false) {
        string progress = terminalProgress ? Vt.ClearProgress : string.Empty;
        if (profile.Protocol == ShellIntegration.None) {
            return progress;
        }

        string cwd = !profile.IsExternal(ExternalIntegration.WorkingDirectory) && !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path)
            ? WorkingDirectoryMarkers.Create(profile, path) : string.Empty;
        string finished = string.Empty;
        if (!profile.IsExternal(ExternalIntegration.OutputEnd) && exitCode is { } code) {
            finished = Vt.OutputEnd(profile.MarkerNumber, code);
        }
        else if (!profile.IsExternal(ExternalIntegration.OutputEnd) && finish) {
            finished = Vt.OutputEnd(profile.MarkerNumber);
        }

        return finished + progress + (profile.ReflowControl && !profile.IsExternal(ExternalIntegration.Reflow) ? Vt.DisableReflow : string.Empty) +
            profile.PromptStart + (profile.Protocol == ShellIntegration.Osc633
                ? Vt.VsCodeWindowsProperty(OperatingSystem.IsWindows()) : string.Empty) + cwd;
    }

    public static string Execute(ShellIntegrationProfile profile, string? command = null, bool terminalProgress = false) {
        string progress = terminalProgress ? Vt.IndeterminateProgress : string.Empty;
        if (profile.Protocol == ShellIntegration.None) {
            return progress;
        }

        string reflow = profile.ReflowControl && !profile.IsExternal(ExternalIntegration.Reflow) ? Vt.EnableReflow : string.Empty;
        if (profile.IsExternal(ExternalIntegration.OutputStart)) { return reflow + progress; }

        string metadata = string.Empty;
        if (profile.Metadata == CommandMetadata.VsCode && command is not null) {
            string nonce = Environment.GetEnvironmentVariable("VSCODE_NONCE") ?? string.Empty;
            bool includeNonce = nonce.Length > 0 && (!OperatingSystem.IsWindows() || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
            string escapedNonce = string.Empty;
            if (includeNonce) {
                escapedNonce = ";" + WorkingDirectoryMarkers.EscapeProperty(nonce);
            }

            metadata = Vt.VsCodeCommand(WorkingDirectoryMarkers.EscapeProperty(command.AsSpan()), escapedNonce);
        }

        if (profile.Metadata == CommandMetadata.Contour && command is not null) {
            return reflow + progress + Vt.ContourCommand(Uri.EscapeDataString(command));
        }

        return reflow + metadata + progress + Vt.OutputStart(profile.MarkerNumber);
    }
}
