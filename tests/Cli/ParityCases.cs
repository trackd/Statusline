using System.Reflection;
using System.Text.Json;

namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static int ParityReport(string name) {
        string report = Path.GetFullPath(Path.Combine("artifacts", "test-results", name));
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(Results));
        return failed == 0 ? 0 : 1;
    }
    private static void ParityUnitCases() {
        Case("LegacyAndTypedCliBlocksShareConfiguration", TypedConfiguration);
        Case("CliVisibilityAndPriorityPreserveImportantBlocks", VisibilityPriority);
        Case("CliHintsReceiveMultilineInputAndDiscardStaleResults", CustomInput);
        Case("CliSymbolColorTracksExitWithoutStatusBlock", SymbolColor);
        Case("CliCustomHintFailuresAndTimeoutsAreObservable", CustomFailures);
        Case("CliTransientBlocksUseSharedFormatting", TransientFormatting);
        Case("CliNullHintCommandsAreRejected", NullHintCommands);
    }

    private static void NullHintCommands() {
        foreach (string definition in new[] { "{\"Kind\":\"git\",\"Commands\":null}", "{\"Executable\":\"hint\",\"Commands\":null}" }) {
            PromptOptions settings = JsonSerializer.Deserialize("{\"EnableHints\":[" + definition + "]}", BlockConfigurationJson.Default.PromptOptions)!;
            bool rejected = false;
            try { using var invalid = new PreparedPrompt(settings); }
            catch (ArgumentException exception) { rejected = exception.Message.Contains("Commands", StringComparison.Ordinal); }
            Check(rejected, "Null Commands must produce a configuration error.");
        }
    }

    private static void TypedConfiguration() {
        using var fixture = new Fixture();
        PromptOptions settings = JsonSerializer.Deserialize("""
            {"Left":["UserHost",{"Kind":"Clock","MinimumWidth":100}],"EnableHints":["git"]}
            """, BlockConfigurationJson.Default.PromptOptions)!;
        using var prompt = new PreparedPrompt(settings);
        Check(Plain(prompt.Render(fixture.Path, 80, 0, null, false)).Contains('@'), "Legacy string block did not render.");
        Check(settings.Left[1].Settings.MinimumWidth == 100 && settings.Left[0].Settings.Kind == "UserHost", "Typed configuration lost fields.");
        bool rejected = false;
        try { using var invalid = new PreparedPrompt(new() { EnableHints = ["typo"] }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unknown hint accepted.");
        using var preset = CliBlock.Hints([new(new() { Kind = "git", Name = "Repository", MinimumWidth = 100 })]).Single();
        Check(preset.Label == "Repository", "Preset resolution discarded block options.");
    }

    private static void VisibilityPriority() {
        using var fixture = new Fixture();
        using var prompt = new PreparedPrompt(new() {
            Left = [new(new() { Kind = "Host", Priority = -10 }), new(new() { Kind = "UserHost", Priority = 10 })],
            Right = [new(new() { Kind = "Clock", MinimumWidth = 100 })], Rows = 2, Style = SegmentStyle.Plain, IconSet = IconSet.None,
        });
        string identity = Environment.UserName + "@" + Environment.MachineName;
        string result = Plain(prompt.Render(fixture.Path, identity.Length + 4, 0, null, true));
        Check(result.Contains(identity, StringComparison.Ordinal) && result.Trim() == identity, "Priority or width filtering failed: " + result);
        using var project = new PreparedPrompt(new() { Left = [new(new() { Kind = "Host", DetectFiles = ["project.flag"], MinimumDurationMilliseconds = 1000 })], Style = SegmentStyle.Plain, IconSet = IconSet.None });
        Check(!Plain(project.Render(fixture.Path, 120, 0, 2000, false)).Contains(Environment.MachineName, StringComparison.Ordinal), "Missing project marker did not hide block.");
        File.WriteAllText(Path.Combine(fixture.Path, "project.flag"), "");
        Check(Plain(project.Render(fixture.Path, 120, 0, 1000, false)).Contains(Environment.MachineName, StringComparison.Ordinal), "Project/duration condition failed.");
        Check(!Plain(project.Render(fixture.Path, 120, 0, 999, false)).Contains(Environment.MachineName, StringComparison.Ordinal), "Duration lower boundary failed.");
    }

    private static BlockSettings HintFixture() {
        string executable = Environment.ProcessPath!;
        string[] arguments = Path.GetFileNameWithoutExtension(executable) == "dotnet"
            ? [Assembly.GetExecutingAssembly().Location, "--hint-fixture"] : ["--hint-fixture"];
        return new() { Name = "Input", Executable = executable, Arguments = arguments, Commands = ["tool"], IntervalMilliseconds = 60000 };
    }

    private static void CustomInput() {
        using var fixture = new Fixture();
        using var prompt = new PreparedPrompt(new() { Left = [], EnableHints = [new(HintFixture())], Style = SegmentStyle.Plain, IconSet = IconSet.None });
        var first = new StatusLineState { InputLine = "tool 'first\nsecond'", InputCursor = 5, InputCommand = "tool" };
        string result = Plain(prompt.Render(fixture.Path, 120, 0, null, false, editor: first));
        Check(result.Contains("HINT:5:tool 'first<newline>second'", StringComparison.Ordinal), "Multiline input/cursor did not reach custom hint: " + result);
        var slow = first with { InputLine = "tool --slow old" };
        _ = prompt.Render(fixture.Path, 120, 0, null, false, wait: false, editor: slow);
        var latest = first with { InputLine = "tool latest", InputCursor = 11 };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        do {
            result = Plain(prompt.Render(fixture.Path, 120, 0, null, false, wait: false, editor: latest));
            Check(!result.Contains("old", StringComparison.Ordinal), "Stale input result was published.");
            if (result.Contains("HINT:11:tool latest", StringComparison.Ordinal)) { break; }
            Thread.Sleep(20);
        } while (clock.Elapsed.TotalSeconds < 5);
        Check(result.Contains("HINT:11:tool latest", StringComparison.Ordinal), "Latest input did not refresh despite the long interval.");
        result = Plain(prompt.Render(fixture.Path, 120, 0, null, false));
        Check(!result.Contains("HINT", StringComparison.Ordinal), "Hint remained outside editing.");
    }

    private static void SymbolColor() {
        using var fixture = new Fixture();
        using var prompt = new PreparedPrompt(new() { Left = [], Symbol = ">", ColorSymbolByExitCode = true });
        string success = prompt.Render(fixture.Path, 80, 0, null, false);
        string failure = prompt.Render(fixture.Path, 80, 1, null, false);
        Check(success != failure && Plain(success) == Plain(failure), "Symbol exit color was incorrectly cached.");
    }

    private static void CustomFailures() {
        using var fixture = new Fixture();
        using var failed = new PreparedPrompt(new() { Left = [], EnableHints = [new(HintFixture())] });
        _ = failed.Render(fixture.Path, 100, 0, null, false, editor: new() { InputLine = "tool --fail", InputCommand = "tool", InputCursor = 11 });
        Check(failed.Errors.Any(error => error.Contains("12", StringComparison.Ordinal) && error.Contains("Hint failure fixture", StringComparison.Ordinal)), "Custom failure diagnostics missing.");
        BlockSettings slow = HintFixture();
        slow.TimeoutMilliseconds = 50;
        using var timed = new PreparedPrompt(new() { Left = [], EnableHints = [new(slow)] });
        var state = new StatusLineState { InputLine = "tool --slow", InputCommand = "tool", InputCursor = 11 };
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        do {
            _ = timed.Render(fixture.Path, 100, 0, null, false, wait: false, editor: state);
            if (timed.Errors.Any()) { break; }
            Thread.Sleep(20);
        } while (deadline.Elapsed.TotalSeconds < 3);
        Check(timed.Errors.Any(error => error.Contains("timed out", StringComparison.Ordinal)), "Custom timeout diagnostics missing.");
    }

    private static void TransientFormatting() {
        using var fixture = new Fixture();
        using var compact = new PreparedPrompt(new PromptOptions {
            TransientBlock = ["Duration"], TransientSymbol = "SHORT ", IconSet = IconSet.None,
            Style = SegmentStyle.Plain, ColorSymbolByExitCode = true,
        }.TransientView());
        string text = Plain(compact.Render(fixture.Path, 80, 0, 2500, false));
        Check(text.Contains("2.5s", StringComparison.Ordinal) && text.EndsWith("SHORT ", StringComparison.Ordinal), "Transient blocks or symbol were lost: " + text);
    }

    private static void ZshLiveHints() {
        using var fixture = new Fixture();
        BlockSettings hint = HintFixture();
        string providerTrace = Path.Combine(fixture.Path, "provider.trace");
        hint.Arguments = [.. hint.Arguments, providerTrace];
        fixture.Configure(new {
            Left = new[] { "Directory" }, EnableHints = new[] { new { hint.Executable, hint.Arguments, hint.Commands, hint.IntervalMilliseconds } },
            Style = "Plain", IconSet = "None", ShellIntegration = "None", IdleTimeoutSeconds = 1, Symbol = " __STATUSLINE_READY__ ",
        });
        using var terminal = new LinuxTerminal("zsh", fixture.Path);
        terminal.Send("typeset -g saved_bindings=\"$(bindkey -L)\"; alias tt=tool\n");
        _ = terminal.ReadFor(200);
        string editorTrace = Path.Combine(fixture.Path, "editor.trace");
        _ = terminal.Command("autoload -Uz add-zle-hook-widget; _parity_trace() { print -r -- \"$_statusline_edit_active|$_statusline_edit_revision|$_statusline_edit_waiting|$BUFFER\" > " + Quote(editorTrace) + "; }; add-zle-hook-widget line-pre-redraw _parity_trace; eval \"$(" + Quote(binary) + " init zsh --config " + Quote(fixture.Config) + ")\"");
        _ = terminal.ReadFor(400);
        terminal.Send("tt argument");
        string output = Plain(terminal.ReadFor(1500));
        if (!output.Contains("HINT:", StringComparison.Ordinal)) {
            output += " EDITOR=" + (File.Exists(editorTrace) ? File.ReadAllText(editorTrace) : "missing") +
                " PROVIDER=" + (File.Exists(providerTrace) ? File.ReadAllText(providerTrace) : "missing");
        }
        Check(output.Contains("HINT:11:tt argument", StringComparison.Ordinal), "Live alias hint did not appear without Enter: " + output);
        terminal.Send("\x03");
        _ = terminal.ReadFor(500);
        string multiline = "tool '😀first\nsecond'";
        terminal.Send("\x1b[200~" + multiline + "\x1b[201~");
        output = Plain(terminal.ReadFor(1500));
        Check(output.Contains("HINT:" + multiline.Length + ":tool '😀first<newline>second'", StringComparison.Ordinal), "Multiline/Unicode snapshot mismatch: " + output);
        terminal.Resize(96);
        output = Plain(terminal.ReadFor(800));
        Check(output.Contains("HINT:", StringComparison.Ordinal), "Idle resize did not repaint hint: " + output);
        terminal.Send("\x03");
        output = Plain(terminal.ReadFor(1500));
        Check(output.Contains("__STATUSLINE_READY__", StringComparison.Ordinal) && !output.Contains("HINT:", StringComparison.Ordinal), "Cancellation did not restore prompt: " + output);
        terminal.Send("tool --slow old");
        _ = terminal.ReadFor(100);
        terminal.Send("\x01\x0btool latest");
        output = Plain(terminal.ReadFor(2000));
        Check(output.Contains("HINT:11:tool latest", StringComparison.Ordinal) && !output.Contains("HINT:15:tool --slow old", StringComparison.Ordinal), "Stale asynchronous hint leaked: " + output);
        terminal.Send("\x03");
        _ = terminal.ReadFor(500);
        terminal.Send("tool 'unfinished");
        output = terminal.ReadFor(500);
        Check(!output.Contains("unmatched", StringComparison.OrdinalIgnoreCase), "Incomplete quote broke redraw hook: " + output);
        terminal.Send("\x03");
        _ = terminal.ReadFor(500);
        string bindingsFile = Path.Combine(fixture.Path, "bindings.ok");
        _ = terminal.Command("[[ $saved_bindings == \"$(bindkey -L)\" ]] && print -r -- valid > " + Quote(bindingsFile));
        Check(File.Exists(bindingsFile) && File.ReadAllText(bindingsFile).Trim() == "valid", "User key bindings changed.");
        _ = terminal.Command(Quote(binary) + " stop --session $_statusline_session");
    }

    private static void ZshIdleResize() {
        using var fixture = new Fixture();
        fixture.Configure(new {
            Target = "HostWritable", Left = new[] { "Status" }, Style = "Plain", IconSet = "None", IdleTimeoutSeconds = 1,
            Symbol = " __STATUSLINE_READY__ ", ShellIntegration = "None",
            StatusLine = new { Left = new[] { "Status" }, Right = new[] { "Clock" }, Style = "Plain", IconSet = "None" },
        });
        using var terminal = new LinuxTerminal("zsh", fixture.Path, "contour");
        string initial = terminal.Command("eval \"$(" + Quote(binary) + " init zsh --config " + Quote(fixture.Config) + ")\"") + terminal.ReadFor(1500);
        Check(StatusFrames(initial).Length > 0, "Initial status frame missing.");
        Check(StatusFrames(terminal.ReadFor(1200)).Length == 0, "Renderer did not pause after idle timeout.");
        terminal.Resize(80);
        string output = terminal.ReadFor(1200);
        Check(StatusFrames(output).Length > 0, "Resize did not wake paused statusline: " + output);
    }
}
