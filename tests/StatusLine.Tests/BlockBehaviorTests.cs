using StatusLine.Blocks.BuiltIns.Project;
using StatusLine.Blocks.Execution;
using Xunit;

namespace StatusLine.Tests;

public sealed class BlockBehaviorTests {
    [Theory]
    [InlineData("bash 5.2", null, "bash 5.2")]
    [InlineData("fish 4.2", "7.6.0", "fish 4.2")]
    [InlineData(null, "7.6.0", "pwsh 7.6.0")]
    [InlineData(" ", "7.6.0", "pwsh 7.6.0")]
    [InlineData(null, null, null)]
    public void ShellUsesHostIdentityOrPowerShellVersion(string? identity, string? powerShellVersion, string? expected) {
        var block = (IImmediateBlock)BlockCatalog.Get(StatusLineBlockKind.Shell);
        var context = new BlockContext { ShellIdentity = identity, PowerShellVersion = powerShellVersion, IconSet = IconSet.None };
        BlockContent? content = block.Evaluate(context);
        Assert.Equal(expected, content?.Text);
        if (expected is not null) { Assert.Equal("SH", content!.Value.Icon); }
    }

    [Theory]
    [InlineData("# branch.head main\n# branch.ab +0 -0", 0, 0, 0, 0, 0, 0)]
    [InlineData("# branch.ab +12 -3", 12, 3, 0, 0, 0, 0)]
    [InlineData("1 M. tracked", 0, 0, 1, 0, 0, 0)]
    [InlineData("1 .M tracked", 0, 0, 0, 1, 0, 0)]
    [InlineData("2 RM renamed", 0, 0, 1, 1, 0, 0)]
    [InlineData("u UU conflict\n? new-file", 0, 0, 0, 0, 1, 1)]
    [InlineData("", 0, 0, 0, 0, 0, 0)]
    [InlineData("# branch.ab invalid\n1 \nunknown\n! ignored", 0, 0, 0, 0, 0, 0)]
    public void GitRecordsDistinguishChangesFromCleanOrIncompleteInput(string text,
        int ahead, int behind, int staged, int modified, int untracked, int conflicts) {
        GitStatus status = GitStatusProvider.Parse(text);
        Assert.Equal(new GitStatus(ahead, behind, staged, modified, untracked, conflicts), status);
        Assert.Equal(staged + modified + untracked + conflicts > 0, status.IsDirty);
    }

    [Theory]
    [InlineData(0.012, "12ms")]
    [InlineData(0.099, "99ms")]
    [InlineData(0.1, "0.1s")]
    [InlineData(0.999, "1.0s")]
    [InlineData(2.53, "2.5s")]
    [InlineData(60, "1m0s")]
    [InlineData(75, "1m15s")]
    [InlineData(95, "1m35s")]
    [InlineData(3725, "62m5s")]
    public void DurationFormattingPreservesThresholds(double seconds, string expected) {
        var block = (IImmediateBlock)BlockCatalog.Get(StatusLineBlockKind.Duration);
        var context = new BlockContext { State = new() { LastDuration = TimeSpan.FromSeconds(seconds) } };
        Assert.Equal(expected, block.Evaluate(context)!.Value.Text);
    }

    [Fact]
    public void DurationOmitsAbsentValuesButRetainsExplicitZero() {
        var block = (IImmediateBlock)BlockCatalog.Get(StatusLineBlockKind.Duration);
        Assert.Null(block.Evaluate(new BlockContext()));
        var context = new BlockContext { State = new() { LastDuration = TimeSpan.Zero, LastExitCode = 3 }, DurationWarning = true };
        Assert.Contains("⚠", block.Evaluate(context)!.Value.Text);
        Assert.Equal("0ms", block.Evaluate(context with { DurationWarning = false })!.Value.Text);
    }

    [Fact]
    public void ProcessMemoryIsTheOnlyProcessWorkingSetKind() {
        Assert.False(Enum.TryParse<StatusLineBlockKind>("Memory", out _));
        Assert.Equal("PROC", BlockCatalog.Get(StatusLineBlockKind.ProcessMemory).Metadata.Icon(IconSet.None));
        Assert.Equal("RAM", BlockCatalog.Get(StatusLineBlockKind.SystemMemory).Metadata.Icon(IconSet.None));
    }
}
