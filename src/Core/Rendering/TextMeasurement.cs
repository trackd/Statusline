namespace StatusLine;

/// <summary>Process-wide renderer trial switch and side-by-side measurement helpers.</summary>
public static class TextMeasurement {
    private static int useUnicode;
    // Legacy remains the default until terminal and allocation comparisons are complete.
    public static bool UseUnicode {
        get => Volatile.Read(ref useUnicode) != 0;
        set => Volatile.Write(ref useUnicode, value ? 1 : 0);
    }
    public static int MeasureLegacy(string text) {
        ArgumentNullException.ThrowIfNull(text);
        return TerminalText.MeasureLegacy(text.AsSpan());
    }
    public static int MeasureUnicode(string text) =>
        Unicode.Measure(text.AsSpan()).Columns;
    public static (string Sequence, int Cells) ClipLegacy(string text, int width) =>
        TerminalText.ClipLegacy(text, width);
    public static (string Sequence, int Cells) ClipUnicode(string text, int width) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        Unicode.MeasurementResult result = Unicode.Measure(text.AsSpan(), 0, int.MaxValue, width);
        return (result.Offset == text.Length ? text : string.Concat(text.AsSpan(0, result.Offset), Vt.Reset), result.Columns);
    }
}
