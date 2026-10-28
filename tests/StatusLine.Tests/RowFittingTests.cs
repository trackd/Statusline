using System.Drawing;
using Xunit;

namespace StatusLine.Tests;

public sealed partial class RowFittingTests {
    private static readonly string LongPath = @"C:\Users\someone\" + new string('x', 150);

    private static StatusLineBlock Block(string text, StatusLineAlignment alignment = StatusLineAlignment.Left, bool shrinkable = false) =>
        new(text, Color.White, Color.SteelBlue, alignment) { Shrinkable = shrinkable };

    [System.Text.RegularExpressions.GeneratedRegex("\x1b\\[[0-9;]*m")]
    private static partial System.Text.RegularExpressions.Regex Sgr();

    private static string Plain(string text) => Sgr().Replace(text, "");

    [Theory]
    [InlineData(SegmentStyle.Plain, false)]
    [InlineData(SegmentStyle.Powerline, true)]
    [InlineData(SegmentStyle.Powerline, false)]
    [InlineData(SegmentStyle.Diamond, true)]
    [InlineData(SegmentStyle.Diamond, false)]
    public void ShortensShrinkableTextWithMiddleEllipsisBeforeDroppingBlocks(SegmentStyle style, bool separator) {
        StatusLineBlock[] blocks = [Block(LongPath, shrinkable: true), Block("git"), Block("12:00")];
        (string sequence, int cells) = StatusLineRenderer.RenderGroup(blocks, StatusLineAlignment.Left, 60, separator, style, roundOuterEdges: true);
        string text = Plain(sequence);
        Assert.True(cells <= 60);
        Assert.Equal(cells, TerminalText.Measure(sequence));
        Assert.Contains(TerminalText.Ellipsis, text);
        Assert.Contains(@"C:\", text);
        Assert.Contains("xxx ", text);
        Assert.Contains("git", text);
        Assert.Contains("12:00", text);
    }

    [Fact]
    public void DropsWholeBlocksFromTheEndOnceTextReachesItsMinimum() {
        StatusLineBlock[] blocks = [Block(LongPath, shrinkable: true), Block("first"), Block("second"), Block("third")];
        (string sequence, int cells) = StatusLineRenderer.RenderGroup(blocks, StatusLineAlignment.Left, 24, false, SegmentStyle.Plain);
        string text = Plain(sequence);
        Assert.True(cells <= 24);
        Assert.Contains("first", text);
        Assert.DoesNotContain("thi", text);
        Assert.DoesNotContain("sec", text);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(200)]
    public void LeftAndRightGroupsKeepAGapInsteadOfOverlapping(int width) {
        StatusLineBlock[] blocks = [
            Block(LongPath, shrinkable: true), Block("user"),
            Block("clock", StatusLineAlignment.Right), Block("host", StatusLineAlignment.Right), Block("date", StatusLineAlignment.Right)];
        string sequence = StatusLineRenderer.RenderBlocks(blocks, width, true, SegmentStyle.Powerline, useCursorPositioning: false, roundOuterEdges: true);
        Assert.True(TerminalText.Measure(sequence) <= width);
        string text = Plain(sequence);
        Assert.Contains("user", text);
        Assert.Contains(TerminalText.Ellipsis, text);
        // Whatever right blocks remain are whole, never clipped into the left group.
        foreach (string right in new[] { "clock", "host", "date" }) {
            if (text.Contains(right[..2], StringComparison.Ordinal)) {
                Assert.Contains(right, text);
            }
        }
    }

    [Theory]
    [InlineData("short", 12, "short")]
    [InlineData("abcdefghijklmnop", 10, "abc…klmnop")]
    [InlineData("abcdef", 1, "…")]
    [InlineData("abcdef", 0, "")]
    public void EllipsizeKeepsMoreOfTheTail(string text, int cells, string expected) {
        Assert.Equal(expected, TerminalText.Ellipsize(text, cells));
        Assert.True(TerminalText.Measure(TerminalText.Ellipsize(text, cells)) <= cells);
    }

    [Fact]
    public void ShortenedBlockIsReusedForTheSameBudget() {
        StatusLineBlock block = Block(LongPath, shrinkable: true);
        Assert.Same(block.Shorten(20), block.Shorten(20));
        Assert.Equal(20, TerminalText.Measure(block.Shorten(20).Text));
    }
}
