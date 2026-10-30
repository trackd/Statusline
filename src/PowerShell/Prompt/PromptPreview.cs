using System.Management.Automation;
using StatusLine.Commands;

namespace StatusLine;

/// <summary>Renders a prompt without installing shell hooks or writing terminal output.</summary>
public static class PromptPreview {
    public static string Render(PromptConfiguration? configuration = null, StatusLineBlock[]? blocks = null,
        int width = 120, int? exitCode = null, TimeSpan? duration = null, bool transient = false, bool includeIntegration = false,
        SessionState? sessionState = null) {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 32767);
        configuration ??= new();
        if (blocks is not null) {
            configuration = configuration with {
                Block = blocks,
                Left = [],
                Right = []
            };
        }

        sessionState ??= LanguagePrimitives.ConvertTo<SessionState>(
            ScriptBlock.Create("$ExecutionContext.SessionState").InvokeReturnAsIs());
        var layout = new PromptLayout(configuration);
        var context = new BlockContext {
            State = StatusLineState.Capture(sessionState, layout.RequiredFacts | (includeIntegration ? SessionFacts.Location : SessionFacts.None)) with { LastExitCode = exitCode, LastDuration = duration },
            PowerShellVersion = CommandHelpers.GetPowerShellVersion(sessionState),
            IconSet = configuration.IconSet,
            BlockTimeoutMilliseconds = configuration.BlockTimeoutMilliseconds,
            DurationWarning = configuration.DurationWarning,
            WeatherLocation = configuration.WeatherLocation,
            SlowProviderWait = TimeSpan.Zero,
        };
        // A preview is one-shot: start every refresh, then wait for them together.
        _ = PromptRenderer.Render(layout, context, width, transient);
        context = context with { SlowProviderWait = TimeSpan.FromMilliseconds(configuration.BlockTimeoutMilliseconds) };
        string prompt = PromptRenderer.Render(layout, context, width, transient);
        if (!includeIntegration) {
            return prompt;
        }

        var profile = ShellIntegrationProfile.Resolve(configuration.ShellIntegration);
        // Redrawing an existing prompt restores A/B, not command completion or directory metadata.
        return (transient ? profile.PromptStart : ShellMarkers.Begin(profile, exitCode, context.State.FileSystemPath, terminalProgress: configuration.TerminalProgress)) +
            prompt + profile.CommandStart;
    }
}
