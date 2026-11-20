using System.Drawing;
using StatusLine.Blocks.BuiltIns.Project;
using StatusLine.Blocks.Execution;
using Xunit;

namespace StatusLine.Tests;

public sealed class ContextFeaturesTests {
    [Fact]
    public void GitDetailsSeparateIndexWorkingTreeAndStash() {
        GitStatus status = GitStatusProvider.Parse("# branch.ab +1 -2\n# stash 3\n1 AM file\n1 .D deleted\n2 R. rename\n? new\nu UU conflict\n");
        Assert.Equal(new GitChanges(1, 0, 0, 1), status.Index);
        Assert.Equal(new GitChanges(1, 1, 1, 0), status.Working);
        Assert.Equal(3, status.Stash);
        BlockContent result = new GitBlock().FormatDetailed("main", status with { Operation = "REBASE" }, IconSet.None);
        Assert.Equal("main ^1 v2 work +1 ~1 -1 staged +1 >1 !1 stash:3 REBASE", result.Text);
        Assert.Equal((TerminalColor)ConsoleColor.DarkRed, result.Accent);
    }

    [Theory]
    [InlineData(79, 1000, false)]
    [InlineData(80, 999, false)]
    [InlineData(80, 1000, true)]
    [InlineData(120, 1000, true)]
    [InlineData(121, 1000, false)]
    public void VisibilityUsesInclusiveWidthAndDurationBounds(int width, int milliseconds, bool visible) {
        using var definition = new StatusLineBlockDefinition("duration", "", TimeSpan.Zero, StatusLineBlockKind.Duration) {
            MinimumWidth = 80, MaximumWidth = 120, MinimumDurationMilliseconds = 1000,
        };
        Assert.Equal(visible, definition.IsVisible(new BlockContext {
            State = new() { LastDuration = TimeSpan.FromMilliseconds(milliseconds) },
        }, width));
        Assert.False(definition.IsVisible(new BlockContext(), width));
    }

    [Fact]
    public void ProjectVisibilityRequiresDetectedFile() {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var definition = new StatusLineBlockDefinition("project", "", TimeSpan.Zero, StatusLineBlockKind.UserHost) {
                DetectFiles = ["project.json"],
            };
            var context = new BlockContext { State = new() { FileSystemPath = directory } };
            Assert.False(definition.IsVisible(context, 100));
            File.WriteAllText(Path.Combine(directory, "project.json"), "{}");
            Assert.True(definition.IsVisible(context, 100));
            Assert.False(definition.IsVisible(context with { State = context.State with { FileSystemPath = null } }, 100));
            Assert.True(BlockSelector.RequiredFacts([new BlockSelector(null, definition)]).HasFlag(SessionFacts.Location));
            Assert.Throws<ArgumentException>(() => definition.DetectFiles = ["../secret"]);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ActivityBlocksOmitEmptyStateAndRenderCapturedValues() {
        BlockContent? Evaluate(StatusLineBlockKind kind, StatusLineState state) =>
            ((IImmediateBlock)BlockCatalog.Get(kind)).Evaluate(new BlockContext { State = state, IconSet = IconSet.None });
        Assert.Null(Evaluate(StatusLineBlockKind.Jobs, new()));
        Assert.Null(Evaluate(StatusLineBlockKind.ViMode, new()));
        Assert.Null(Evaluate(StatusLineBlockKind.RunningCommand, new()));
        Assert.Equal("2", Evaluate(StatusLineBlockKind.Jobs, new() { RunningJobCount = 2 })!.Value.Text);
        Assert.Equal("NORMAL", Evaluate(StatusLineBlockKind.ViMode, new() { ViMode = "NORMAL" })!.Value.Text);
        var started = DateTimeOffset.UtcNow;
        var context = new BlockContext { Timestamp = started.AddSeconds(5), State = new() { RunningCommand = "build", CommandStartedAt = started } };
        Assert.Equal("build 5.0s", ((IImmediateBlock)BlockCatalog.Get(StatusLineBlockKind.RunningCommand)).Evaluate(context)!.Value.Text);
        string text = Evaluate(StatusLineBlockKind.UserHost, new())!.Value.Text;
        Assert.EndsWith("@" + Environment.MachineName, text);
        Assert.True(text.IndexOf('@') > 0);
    }
}
