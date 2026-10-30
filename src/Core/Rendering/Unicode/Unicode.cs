namespace StatusLine;

// Grapheme rules and widths follow Windows Terminal; VT skipping is optional.
public static partial class Unicode {
    public readonly record struct MeasurementResult(int Offset, int CursorMovements, int Columns, bool ContainsVT);
    public static MeasurementResult Measure(string str, int offset = 0,
        int maxCursorMovements = int.MaxValue, int maxColumns = int.MaxValue, bool skipVT = true) {
        ArgumentNullException.ThrowIfNull(str);
        return Measure(str.AsSpan(), offset, maxCursorMovements, maxColumns, skipVT);
    }
    public static MeasurementResult Measure(ReadOnlySpan<char> str, int offset = 0,
        int maxCursorMovements = int.MaxValue, int maxColumns = int.MaxValue, bool skipVT = true) {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, str.Length);
        int cursorMovements = 0;
        int columns = 0;
        bool sawVT = false;
        int length = str.Length;

        if (offset < length && maxCursorMovements > 0 && maxColumns > 0) {
            if (skipVT) {
                while (offset < length && TrySkipVTForward(str, ref offset)) {
                    sawVT = true;
                }
            }

            if (offset < length) {
                GraphemeState state = new(offset);

                while (offset < length) {
                    _ = GraphemeNext(ref state, str, skipVT, out bool clusterContainsVT);
                    sawVT |= clusterContainsVT;

                    if (state.Len == 0) {
                        break;
                    }

                    int width = state.Width;
                    int clusterEnd = state.Beg + state.Len;

                    if (columns + width > maxColumns || cursorMovements + 1 > maxCursorMovements) {
                        break;
                    }

                    offset = clusterEnd;
                    cursorMovements++;
                    columns += width;

                    if (offset >= length) {
                        break;
                    }
                }
            }
        }

        return new MeasurementResult(offset, cursorMovements, columns, sawVT);
    }

    public static int UnicodeLength(string input) => Measure(input.AsSpan()).Columns;
    public static int UnicodeCells(string input) => Measure(input.AsSpan()).CursorMovements;
}
