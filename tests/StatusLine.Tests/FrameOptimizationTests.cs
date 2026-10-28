using System.Drawing;
using StatusLine.Blocks.Execution;
using Xunit;

namespace StatusLine.Tests;

public sealed class FrameOptimizationTests {
    [Fact]
    public void PromptGroupsOverrideDefinitionAlignmentWithoutCreatingAnIntermediateBlock() {
        using var definition = new StatusLineBlockDefinition("history", "", TimeSpan.Zero, StatusLineBlockKind.HistoryId) {
            Alignment = StatusLineAlignment.Right,
        };
        BlockSelector[] selection = BlockSelector.Parse([definition]);
        var context = new BlockContext { State = new() { HistoryId = 42 } };
        var appearance = new BlockOverrides(StatusLineAlignment.Left, null, null, " ");
        var blocks = new List<StatusLineBlock>();
        BlockEvaluator.AppendTo(blocks, selection, context, appearance, StatusLineAlignment.Left);
        StatusLineBlock first = Assert.Single(blocks);
        Assert.Equal(StatusLineAlignment.Left, first.Alignment);
        blocks.Clear();
        BlockEvaluator.AppendTo(blocks, selection, context, appearance, StatusLineAlignment.Left);
        Assert.Same(first, Assert.Single(blocks));
    }

    [Fact]
    public void GradientAppearanceReuseInvalidatesWhenSourceBlockChanges() {
        var cache = new BlockGradientCache(Color.Red, Color.Blue, SegmentStyle.Powerline);
        StatusLineBlock[] blocks = [new("one", default, default), new("two", default, default)];
        IReadOnlyList<StatusLineBlock> first = cache.Apply(blocks);
        Assert.Same(first, cache.Apply(blocks));
        blocks[0] = blocks[0].WithAppearance(StatusLineAlignment.Right, Color.Green, default);
        IReadOnlyList<StatusLineBlock> changed = cache.Apply(blocks);
        Assert.NotSame(first, changed);
        Assert.Equal(StatusLineAlignment.Right, changed[0].Alignment);
        Assert.Equal("one", changed[0].Text);
        Assert.Same(first[1], changed[1]);
    }

    [Theory]
    [InlineData(SegmentStyle.Plain, false)]
    [InlineData(SegmentStyle.Powerline, false)]
    [InlineData(SegmentStyle.Diamond, false)]
    [InlineData(SegmentStyle.Rounded, false)]
    [InlineData(SegmentStyle.Powerline, true)]
    [InlineData(SegmentStyle.Diamond, true)]
    public void DirectFramingPreservesEveryProtocolByte(SegmentStyle style, bool blend) {
        StatusLineBlock[] blocks = [
            new("日本", Color.Red, default, StatusLineAlignment.Left),
            new("middle", Color.Green, default, StatusLineAlignment.Center),
            new("right", Color.Blue, default, StatusLineAlignment.Right),
        ];
        foreach (int width in new[] { 4, 40 }) {
            string bare = StatusLineRenderer.RenderBlocks(blocks, width, false, style, blendBackground: blend);
            Assert.Equal(Vt.StatusText(bare, false), StatusLineRenderer.RenderBlocks(blocks, width, false, style,
                blendBackground: blend, framing: StatusLineFraming.HostWritable));
            Assert.Equal(Vt.StatusText(bare, true), StatusLineRenderer.RenderBlocks(blocks, width, false, style,
                blendBackground: blend, framing: StatusLineFraming.InitializeHostWritable));
            Assert.Equal(bare + Vt.Reset, StatusLineRenderer.RenderBlocks(blocks, width, false, style,
                blendBackground: blend, framing: StatusLineFraming.Reset));
        }
    }

    [Fact]
    public void FrameReuseInvalidatesOnRenderingInputs() {
        UpdaterOptions options = Options();
        var output = new StatusLineOutput(options, CancellationToken.None);
        StatusLineBlock[] blocks = [new("state", Color.Red, default)];
        Assert.True(output.Render(blocks, 80, StatusLineFraming.Reset));
        string frame = output.Frame.ToString();
        Assert.False(output.Render(blocks, 80, StatusLineFraming.Reset));
        Assert.Equal(frame, output.Frame.ToString());
        Assert.True(output.Render(blocks, 60, StatusLineFraming.Reset));
        blocks[0] = blocks[0].AsStale("failure");
        Assert.True(output.Render(blocks, 60, StatusLineFraming.Reset));
        Assert.True(output.Render(blocks, 60, StatusLineFraming.HostWritable));
        Assert.True(output.Render(blocks, 60, StatusLineFraming.Reset));
        output.UpdateOptions(options with { StartColor = Color.Green });
        Assert.True(output.Render(blocks, 60, StatusLineFraming.Reset));
        // A re-render that produces identical text is not a change.
        blocks[0] = new("state", Color.Red, default);
        Assert.True(output.Render(blocks, 60, StatusLineFraming.Reset));
        blocks[0] = new("state", Color.Red, default);
        Assert.False(output.Render(blocks, 60, StatusLineFraming.Reset));
        bool unicode = TextMeasurement.UseUnicode;
        try {
            output.Render(blocks, 60, StatusLineFraming.Reset);
            TextMeasurement.UseUnicode = !unicode;
            output.Render(blocks, 60, StatusLineFraming.Reset);
            Assert.True(output.Render(blocks, 61, StatusLineFraming.Reset));
        }
        finally {
            TextMeasurement.UseUnicode = unicode;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingFramesRenderWithoutAllocating(bool blend) {
        UpdaterOptions options = blend ? Options() : Options() with { BlendBackground = false, StartColor = Color.Empty, EndColor = Color.Empty };
        var output = new StatusLineOutput(options, CancellationToken.None);
        StatusLineBlock[] first = [new("one", Color.Red, default), new("two", default, default, StatusLineAlignment.Right)];
        StatusLineBlock[] second = [new("uno", Color.Red, default), new("dos", default, default, StatusLineAlignment.Right)];
        for (int index = 0; index < 100; index++) {
            output.Render(index % 2 == 0 ? first : second, 120, StatusLineFraming.HostWritable);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++) {
            Assert.True(output.Render(index % 2 == 0 ? first : second, 120, StatusLineFraming.HostWritable));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static UpdaterOptions Options() => new(
        Selection: [], Overrides: new BlockOverrides(null, null, null, " "),
        Interval: TimeSpan.FromSeconds(1), BlockSeparator: false, Style: SegmentStyle.Powerline,
        IconSet: IconSet.None, SessionState: null!, PowerShellVersion: null,
        Target: StatusLineTarget.File, OutputPath: string.Empty, RunspaceId: Guid.Empty,
        StartColor: Color.Red, EndColor: Color.Blue, BlockTimeoutMilliseconds: 2000,
        UpdateDuringCommand: false, IdleTimeoutSeconds: 0, BlendBackground: true);
}
