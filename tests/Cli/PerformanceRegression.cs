using System.Text;

namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static void PerformanceUnitCases() {
        Case("PreparedPromptReusesFramesAndTracksChangedInputs", PreparedRendering);
        Case("BufferedFieldsPreserveUnicodeBoundariesAndLimits", () => BufferedFields().GetAwaiter().GetResult());
        Case("ConditionalStatusFilePublicationRejectsStaleFrames", ConditionalFile);
    }

    private static void PreparedRendering() {
        using var fixture = new Fixture();
        var layout = new PreparedPrompt(new() {
            Left = ["Directory", "Status", "Duration"],
            Style = SegmentStyle.Plain,
            IconSet = IconSet.None,
            DirectoryStyle = DirectoryDisplayStyle.Leaf,
        });
        string first = layout.Render(fixture.Path, 100, 0, 12, false);
        Check(Plain(first).Contains(Path.GetFileName(fixture.Path), StringComparison.Ordinal), "Directory missing from prepared prompt.");
        Check(Plain(first).Contains("OK", StringComparison.Ordinal) && Plain(first).Contains("12ms", StringComparison.Ordinal), "Command state missing.");
        Check(ReferenceEquals(first, layout.Render(fixture.Path, 100, 0, 12, false)), "Unchanged prompt was rebuilt.");
        string failed = layout.Render(fixture.Path, 100, 7, 1200, false);
        Check(Plain(failed).Contains("ERR 7", StringComparison.Ordinal) && Plain(failed).Contains("1.2s", StringComparison.Ordinal), "Changed command state was cached incorrectly.");
        string narrow = layout.Render(fixture.Path, 28, 7, 1200, false);
        Check(TerminalText.Measure(narrow) <= 27, "Resized prompt exceeds its width.");
        Check(!ReferenceEquals(failed, narrow), "Width change reused the wrong frame.");
        string row = layout.Render(fixture.Path, 100, 7, 1200, true);
        Check(!ReferenceEquals(row, failed) && ReferenceEquals(row, layout.Render(fixture.Path, 100, 7, 1200, true)), "Status row cache is not separate from prompt framing.");
        bool rejected = false;
        try { _ = new PreparedPrompt(new() { Left = ["Unknown"] }); }
        catch (ArgumentException) { rejected = true; }

        Check(rejected, "Invalid block was not rejected when compiling the layout.");
        bool previousUnicode = TextMeasurement.UseUnicode;
        try {
            string unicodePath = Directory.CreateDirectory(Path.Combine(fixture.Path, "👩‍💻")).FullName;
            var configuration = new PromptOptions {
                Left = ["Directory"],
                Right = ["Status"],
                Rows = 2,
                Style = SegmentStyle.Plain,
                IconSet = IconSet.None,
                DirectoryStyle = DirectoryDisplayStyle.Leaf,
            };
            var changing = new PreparedPrompt(configuration);
            TextMeasurement.UseUnicode = false;
            _ = changing.Render(unicodePath, 30, 0, null, false);
            TextMeasurement.UseUnicode = true;
            string reused = changing.Render(unicodePath, 30, 0, null, false);
            string fresh = new PreparedPrompt(configuration).Render(unicodePath, 30, 0, null, false);
            Check(reused == fresh, "Unicode measurement changes reused stale block widths.");
        }
        finally { TextMeasurement.UseUnicode = previousUnicode; }
    }

    private static async Task BufferedFields() {
        string longField = new('x', 65536);
        string payload = "\0Malmö/🦊\nnext\0" + longField + "\0tail\0";
        using (var source = new FragmentedInput(Encoding.UTF8.GetBytes(payload), 3))
        using (var reader = new ShellFieldReader(source, 65536)) {
            Check(await reader.ReadStringAsync(CancellationToken.None) == "", "Empty field lost.");
            Check(await reader.MatchesAsync("Malmö/🦊\nnext", CancellationToken.None), "Fragmented Unicode field changed.");
            Check(await reader.ReadStringAsync(CancellationToken.None) == longField, "Exactly 64 KiB should be accepted.");
            Check(await reader.ReadStringAsync(CancellationToken.None) == "tail", "Buffered next field was lost.");
            bool ended = false;
            try { _ = await reader.ReadAsync(CancellationToken.None); }
            catch (EndOfStreamException) { ended = true; }

            Check(ended, "EOF was not reported.");
        }

        using (var source = new MemoryStream(Encoding.UTF8.GetBytes("different\0next\0")))
        using (var reader = new ShellFieldReader(source, 65536)) {
            Check(!await reader.MatchesAsync("expected", CancellationToken.None), "Environment mismatch was ignored.");
            Check(await reader.ReadStringAsync(CancellationToken.None) == "next", "Mismatch left the next field misaligned.");
        }

        foreach (bool terminated in new[] { true, false }) {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 65537) + (terminated ? "\0" : "")));
            using var reader = new ShellFieldReader(source, 65536);
            bool rejected = false;
            try { _ = await reader.ReadAsync(CancellationToken.None); }
            catch (IOException exception) when (exception is not EndOfStreamException) { rejected = true; }

            Check(rejected, "Oversized field accepted.");
        }

        using (var source = new MemoryStream(Encoding.UTF8.GetBytes("first\0second\0")))
        using (var reader = new ShellFieldReader(source, 65536))
        using (var cancellation = new CancellationTokenSource()) {
            _ = await reader.ReadAsync(CancellationToken.None);
            cancellation.Cancel();
            bool cancelled = false;
            try { _ = await reader.ReadAsync(cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }

            Check(cancelled, "Cancellation was ignored for a buffered field.");
        }
    }

    private sealed class FragmentedInput(byte[] bytes, int fragmentSize) : MemoryStream(bytes) {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(fragmentSize, buffer.Length)], cancellationToken);
    }

    private static void ConditionalFile() {
        using var fixture = new Fixture();
        string file = Path.Combine(fixture.Path, "status.txt");
        Check(StatusFile.TryWrite(file, "current"), "Initial status write failed.");
        Check(!StatusFile.TryWriteIfCurrent(file, "obsolete", static (_, _) => false), "Superseded frame was published.");
        Check(File.ReadAllText(file) == "current", "Superseded frame replaced current output.");
        Check(Directory.GetFiles(fixture.Path, "*.tmp.*").Length == 0, "Superseded temporary file leaked.");
        Check(StatusFile.TryWriteIfCurrent(file, "new", static (temporary, path) => { File.Move(temporary, path, true); return true; }), "Current frame was not published.");
        Check(File.ReadAllText(file) == "new", "Published frame content is wrong.");
        string missing = Path.Combine(fixture.Path, "missing", "status.txt");
        Check(!StatusFile.TryWrite(missing, "retry") && StatusFile.LastError is not null, "Write failure was not reported.");
        Directory.CreateDirectory(Path.GetDirectoryName(missing)!);
        Check(StatusFile.TryWrite(missing, "retry") && StatusFile.LastError is null && File.ReadAllText(missing) == "retry", "Failed write could not recover.");
    }

    private static void StableStatusFile() {
        using var fixture = new Fixture();
        fixture.Configure(new {
            IdleTimeoutSeconds = 0,
            Left = new[] { "Status" },
            StatusLine = new { Left = new[] { "Status" }, Style = "Plain", IconSet = "None" }
        });
        string file = Path.Combine(fixture.Path, "status.txt");
        string session = Start(fixture, statusFile: file);
        _ = Render(session, fixture.Path);
        Until(() => File.Exists(file) && ReadStatusFile(file).Contains("OK", StringComparison.Ordinal), "Initial frame missing.");
        DateTime written = File.GetLastWriteTimeUtc(file);
        Thread.Sleep(1000);
        Check(File.GetLastWriteTimeUtc(file) == written, "Unchanged status file was rewritten.");
        _ = Success("prompt", "--session", session, "--cwd", fixture.Path, "--status", "23");
        Until(() => ReadStatusFile(file).Contains("ERR 23", StringComparison.Ordinal), "Changed command state was not published.");
        File.Delete(file);
        Directory.CreateDirectory(file);
        Until(() => Info(session).GetProperty("StatusFileError").ValueKind != System.Text.Json.JsonValueKind.Null, "Write error missing from worker diagnostics.");
        Directory.Delete(file);
        File.WriteAllText(file, "external recovery placeholder");
        Until(() => File.Exists(file) && ReadStatusFile(file).Contains("ERR 23", StringComparison.Ordinal), "Worker did not retry a failed unchanged frame.");
        Check(Info(session).GetProperty("StatusFileError").ValueKind == System.Text.Json.JsonValueKind.Null, "Recovered file error was not cleared.");
    }
}
