using System.Drawing;
using Xunit;

namespace StatusLine.Tests;

public sealed class TerminalParsingTests {
    public static TheoryData<string> ControlStrings() {
        var data = new TheoryData<string>();
        foreach (char introducer in new[] { ']', 'P', 'X', '^', '_' }) {
            foreach (bool c1 in new[] { false, true }) {
                string start = c1 ? ((char)(introducer + 0x40)).ToString() : $"\e{introducer}";
                data.Add(start + "data\e\\");
                data.Add(start + "data\u009c");
                data.Add(start + "data\a" + (introducer == ']' ? "" : "payload\e\\"));
                data.Add(start + "data\u0018");
                data.Add(start + "data\u001a");
                data.Add(start + "unterminated");
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ControlStrings))]
    public void ControlStrings_AreSkippedForMeasurementButNeutralizedForSanitization(string sequence) {
        bool unterminated = sequence.EndsWith("unterminated", StringComparison.Ordinal);
        string text = "A" + sequence + (unterminated ? "" : "B");
        int expectedCells = unterminated ? 1 : 2;

        Unicode.MeasurementResult result = Unicode.Measure(text);

        Assert.Equal(expectedCells, result.Columns);
        Assert.Equal(expectedCells, result.CursorMovements);
        Assert.Equal(text.Length, result.Offset);
        Assert.True(result.ContainsVT);
        Assert.Equal(expectedCells, TerminalText.MeasureLegacy(text));
        Assert.Equal(unterminated ? "A " : "A B", TerminalText.Sanitize(text, keepStyles: true));
        Assert.Equal(unterminated ? "A " : "A B", TerminalText.Sanitize(text, keepStyles: false));
        Assert.Equal(1 + sequence.Length, Unicode.Measure(text, maxColumns: 1).Offset);
        (string clipped, int cells) = TerminalText.ClipLegacy(text, 1);
        Assert.Equal(1, cells);
        Assert.Equal("A" + sequence + (unterminated ? "" : Vt.Reset), clipped);

        Unicode.MeasurementResult leading = Unicode.Measure(text.AsSpan(1));
        Assert.Equal(expectedCells - 1, leading.Columns);
        Assert.Equal(expectedCells - 1, leading.CursorMovements);
        Assert.Equal(text.Length - 1, leading.Offset);
        Assert.True(leading.ContainsVT);
    }

    [Theory]
    [InlineData("\ePdata\e[31m")]
    [InlineData("\u0098data\e[31m")]
    [InlineData("\e[12\e[31m")]
    public void InterruptedSequences_ResumeAtTheNextEscape(string sequence) {
        string text = "A" + sequence + "B";

        Assert.Equal(2, Unicode.Measure(text).Columns);
        Assert.Equal(2, TerminalText.MeasureLegacy(text));
        Assert.Equal("A \e[31mB", TerminalText.Sanitize(text, keepStyles: true));
    }

    [Theory]
    [InlineData("\e[38;2;255;255;255m", "\e[38;2;255;255;255m")]
    [InlineData("\u009b38;5;196m", "\e[38;5;196m")]
    public void Sanitization_RetainsOnlyRequestedStylesAndNormalizesC1(string sequence, string normalized) {
        Assert.Equal(normalized + "X", TerminalText.Sanitize(sequence + "X", keepStyles: true));
        Assert.Equal(" X", TerminalText.Sanitize(sequence + "X", keepStyles: false));
        Assert.Equal(1, Unicode.Measure(sequence + "X").Columns);
    }

    [Theory]
    [InlineData(-1, int.MaxValue, int.MaxValue)]
    [InlineData(2, int.MaxValue, int.MaxValue)]
    [InlineData(-1, 0, int.MaxValue)]
    [InlineData(2, 0, int.MaxValue)]
    [InlineData(-1, int.MaxValue, 0)]
    [InlineData(2, int.MaxValue, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(int.MaxValue, 0, 0)]
    public void Measure_InvalidOffsetsAlwaysThrow(int offset, int cursorBudget, int columnBudget) {
        foreach (bool skipVT in new[] { false, true }) {
            Assert.Throws<ArgumentOutOfRangeException>("offset",
                () => Unicode.Measure("X", offset, cursorBudget, columnBudget, skipVT));
            Assert.Throws<ArgumentOutOfRangeException>("offset",
                () => Unicode.Measure("X".AsSpan(), offset, cursorBudget, columnBudget, skipVT));
        }
    }

    [Theory]
    [InlineData("", 0, int.MaxValue, int.MaxValue)]
    [InlineData("X", 1, int.MaxValue, int.MaxValue)]
    [InlineData("X", 0, 0, int.MaxValue)]
    [InlineData("X", 0, int.MaxValue, 0)]
    [InlineData("X", 1, 0, 0)]
    public void Measure_ValidEmptyRangesAndZeroBudgetsPreserveOffset(string text, int offset, int cursorBudget, int columnBudget) {
        var expected = new Unicode.MeasurementResult(offset, 0, 0, false);
        foreach (bool skipVT in new[] { false, true }) {
            Assert.Equal(expected, Unicode.Measure(text, offset, cursorBudget, columnBudget, skipVT));
            Assert.Equal(expected, Unicode.Measure(text.AsSpan(), offset, cursorBudget, columnBudget, skipVT));
        }
    }

    [Theory]
    [InlineData("38;2;255;255;255")]
    [InlineData("38;2;0;0;0;48;2;0;0;0;58;5;0")]
    [InlineData("38;5;196")]
    [InlineData("31")]
    [InlineData("97")]
    public void Gradient_ExplicitForegroundIsNotOverridden(string parameters) {
        string style = $"\e[{parameters}m";
        string output = RenderWhiteGradient(style + "X");

        Assert.Contains(style + "\e[48;2;255;255;255mX", output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("39")]
    [InlineData("38;5;196;0")]
    [InlineData("38;2;255;255;255;39")]
    [InlineData("38;5;196;")]
    [InlineData("31;")]
    public void Gradient_ForegroundResetRestoresAutomaticContrast(string reset) {
        string resetStyle = $"\e[{reset}m";
        string output = RenderWhiteGradient("\e[38;5;196mX" + resetStyle + "Y");

        Assert.Contains(resetStyle + "\e[38;2;0;0;0;48;2;255;255;255mY", output);
    }

    private static string RenderWhiteGradient(string text) {
        StatusLineBlock[] blocks = [new(text, default, default, padding: "")];
        return StatusLineGradient.Render(blocks, 8, SegmentStyle.Plain, Color.White, Color.White,
            TextContrastMode.Automatic, roundOuterEdges: false);
    }
}
