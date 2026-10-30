using System.Globalization;
using System.Text;

namespace StatusLine;

internal static class TerminalText {
    public static string Sanitize(string text, bool keepStyles) {
        ReadOnlySpan<char> value = text.AsSpan();
        int first = -1;
        for (int index = 0; index < value.Length; index++) {
            if (char.IsControl(value[index])) {
                first = index;
                break;
            }
        }

        if (first < 0) {
            return text;
        }

        StringBuilder builder = StringBuilderCache.Rent(text.Length);
        try {
            builder.Append(value[..first]);
            AppendSanitized(builder, value[first..], keepStyles);
            return builder.ToString();
        }
        finally {
            StringBuilderCache.Return(builder);
        }
    }

    // Measurement skips VT; sanitization separately allows only safe SGR and neutralizes controls.
    internal static void AppendSanitized(StringBuilder builder, ReadOnlySpan<char> value, bool keepStyles) {
        for (int index = 0; index < value.Length;) {
            if (IsEscape(value[index])) {
                int length = EscapeLength(value[index..], out bool sgr);
                if (sgr && keepStyles) {
                    if (value[index] == Vt.ControlSequence8Bit) {
                        builder.Append(Vt.Csi).Append(value.Slice(index + 1, length - 1));
                    }
                    else {
                        builder.Append(value.Slice(index, length));
                    }
                }
                else {
                    builder.Append(' ');
                }

                index += length;
            }
            else {
                builder.Append(char.IsControl(value[index]) ? ' ' : value[index]);
                index++;
            }
        }
    }

    public static (string Sequence, int Cells) Clip(string text, int limit) {
        ArgumentNullException.ThrowIfNull(text);
        (int length, int cells) = ClipLength(text.AsSpan(), Math.Max(0, limit));
        return (length == text.Length ? text : string.Concat(text.AsSpan(0, length), Vt.Reset), cells);
    }

    internal static (int Length, int Cells) ClipLength(ReadOnlySpan<char> value, int limit) {
        if (!TextMeasurement.UseUnicode) {
            return ClipLengthLegacy(value, limit);
        }

        Unicode.MeasurementResult result = Unicode.Measure(value, 0, int.MaxValue, limit);
        return (result.Offset, result.Columns);
    }

    internal static (string Sequence, int Cells) ClipLegacy(string text, int limit) {
        ArgumentNullException.ThrowIfNull(text);
        (int length, int cells) = ClipLengthLegacy(text.AsSpan(), Math.Max(0, limit));
        return (length == text.Length ? text : string.Concat(text.AsSpan(0, length), Vt.Reset), cells);
    }

    private static (int Length, int Cells) ClipLengthLegacy(ReadOnlySpan<char> value, int limit) {
        int index = 0;
        int cells = 0;
        while (index < value.Length) {
            if (IsEscape(value[index])) {
                index += EscapeLength(value[index..], out _);
                continue;
            }

            if (value[index] is >= ' ' and <= '~' && (index + 1 == value.Length || value[index + 1] is (>= ' ' and <= '~') or Vt.Escape)) {
                int runLength = value[index..].IndexOfAnyExceptInRange(' ', '~');
                if (runLength < 0) {
                    runLength = value.Length - index;
                }

                if (index + runLength < value.Length && value[index + runLength] != Vt.Escape) {
                    runLength = Math.Max(0, runLength - 1);
                }

                if (runLength > 0) {
                    int take = Math.Min(runLength, Math.Max(0, limit - cells));
                    index += take;
                    cells += take;
                    if (take < runLength) {
                        return (index, cells);
                    }

                    continue;
                }
            }

            int length = StringInfo.GetNextTextElementLength(value[index..]);
            int width = CellWidth.Of(value.Slice(index, length));
            if (cells + width > limit) {
                return (index, cells);
            }

            cells += width;
            index += length;
        }

        return (value.Length, cells);
    }

    public static int Measure(ReadOnlySpan<char> value) => TextMeasurement.UseUnicode
        ? Unicode.Measure(value).Columns : MeasureLegacy(value);

    internal const string Ellipsis = "…";

    /// <summary>Shortens plain text to <paramref name="cells"/> with a middle ellipsis, keeping more of the tail.</summary>
    internal static string Ellipsize(string text, int cells) {
        ReadOnlySpan<char> value = text.AsSpan();
        int total = Measure(value);
        if (total <= cells) {
            return text;
        }

        if (cells <= 1) {
            return cells == 1 ? Ellipsis : string.Empty;
        }

        (int headLength, int headCells) = ClipLength(value, (cells - 1) / 3);
        int tailBudget = cells - 1 - headCells;
        int remaining = Measure(value[headLength..]);
        int index = headLength;
        while (index < value.Length && remaining > tailBudget) {
            int length = StringInfo.GetNextTextElementLength(value[index..]);
            remaining -= Measure(value.Slice(index, length));
            index += length;
        }

        return string.Concat(value[..headLength], Ellipsis, value[index..]);
    }

    internal static int MeasureLegacy(ReadOnlySpan<char> value) {
        int cells = 0;
        while (!value.IsEmpty) {
            if (IsEscape(value[0])) {
                value = value[EscapeLength(value, out _)..];
                continue;
            }

            int ascii = value.IndexOfAnyExceptInRange(' ', '~');
            if (ascii < 0) {
                return cells + value.Length;
            }
            // Keep the last ASCII character with a following combining character.
            if (ascii > 1) {
                cells += ascii - 1;
                value = value[(ascii - 1)..];
            }

            int length = StringInfo.GetNextTextElementLength(value);
            cells += CellWidth.Of(value[..length]);
            value = value[length..];
        }

        return cells;
    }

    private static bool IsEscape(char character)
        => TerminalSequence.IsEscape(character);

    internal static int EscapeLength(ReadOnlySpan<char> value, out bool sgr)
        => TerminalSequence.Length(value, out sgr);
}
