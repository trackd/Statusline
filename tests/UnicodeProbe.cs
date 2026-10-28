using System;
using System.Collections.Generic;

// Oracle port of microsoft/terminal 7d06b26f73ef4bcb78b56647c395251eeee4c2bc,
// CodepointWidthDetector.cpp::_graphemeNext, preserving its goto-based flow.
// Tests compile the production sources in UnicodeUnderTest to compare single-input traversal.
public static class UnicodeProbe {
    private struct OracleState {
        public int Begin, Length, Width, Last;
        public uint State;
    }

    private static bool Step(ref OracleState s, ReadOnlySpan<char> text) {
        int begin = s.Begin + s.Length;
        int end = begin;
        if (end < text.Length) {
            int width = s.Width, lead = s.Last, next, trail, w;
            uint state = ~s.State, cp = 0;
            if (s.State != 0) {
                goto FetchNext;
            }
            end = Next(text, end, out cp);
            lead = UnicodeUnderTest.Unicode.OracleLookup(cp);
            width = 0;
            state = 0;
        AddWidth:
            w = lead >> 6;
            if (w == 3) {
                w = 1;
            }
            if (cp == 0xFE0F) {
                w = 2;
            }
            width += w;
            if (end >= text.Length) {
                goto Finish;
            }
        FetchNext:
            next = Next(text, end, out cp);
            trail = UnicodeUnderTest.Unicode.OracleLookup(cp);
            state = UnicodeUnderTest.Unicode.OracleJoin(state, lead, trail);
            if (state == 3) {
                state = ~0u;
                lead = 0;
                goto Finish;
            }
            end = next;
            lead = trail;
            goto AddWidth;
        Finish:
            s.Begin = begin;
            s.Length = end - begin;
            s.Width = width > 2 ? 2 : width;
            s.State = ~state;
            s.Last = lead;
        }
        return end < text.Length;
    }

    private static int Next(ReadOnlySpan<char> text, int offset, out uint cp) {
        uint c = text[offset++];
        if ((c & 0xF800) == 0xD800) {
            uint first = c;
            c = 0xFFFD;
            if ((first & 0x400) == 0 && offset < text.Length) {
                uint second = text[offset];
                if ((second & 0xFC00) == 0xDC00) {
                    c = (first << 10) - 0x35FDC00 + second;
                    offset++;
                }
            }
        }
        cp = c;
        return offset;
    }

    private static void CompareStep(ref OracleState expected, ref UnicodeUnderTest.Unicode.GraphemeState actual,
        ReadOnlySpan<char> text, ref int comparisons) {
        bool expectedMore = Step(ref expected, text);
        bool actualMore = UnicodeUnderTest.Unicode.ProbeNext(ref actual, text);
        if (expectedMore != actualMore || expected.Begin != actual.Beg || expected.Length != actual.Len ||
            expected.Width != actual.Width || expected.State != actual.State || expected.Last != actual.Last) {
            throw new InvalidOperationException("Windows Terminal iterator state differs.");
        }
        comparisons++;
    }

    public static int CompareIterator() {
        var samples = new List<string> { "", "abc", "日本", "a\u0301", "❤️", "\ue0b0", "👩‍👩‍👦", "🇸🇪🇳", "\ud800", "\udc00" };
        string[] parts = { "a", "日", "\u0301", "❤", "\ufe0f", "👩", "\u200d", "🇸", "🇪", "\ue0b0" };
        var random = new Random(1729);
        for (int i = 0; i < 500; i++) {
            string text = "";
            for (int j = 0, length = random.Next(1, 15); j < length; j++) {
                text += parts[random.Next(parts.Length)];
            }
            samples.Add(text);
        }
        int comparisons = 0;
        foreach (string sample in samples) {
            OracleState expected = default;
            var actual = new UnicodeUnderTest.Unicode.GraphemeState(0);
            do {
                CompareStep(ref expected, ref actual, sample, ref comparisons);
            } while (actual.Beg + actual.Len < sample.Length);
        }
        return comparisons;
    }

}

namespace UnicodeUnderTest {
    public static partial class Unicode {
        // Same partial class, compiled only in tests: no production visibility change.
        internal static bool ProbeNext(ref GraphemeState state, ReadOnlySpan<char> text) =>
            GraphemeNext(ref state, text, false, out _);
        internal static int OracleLookup(uint cp) => UcdLookup(cp);
        internal static uint OracleJoin(uint state, int lead, int trail) => UcdGraphemeJoins(state, lead, trail);
    }
}
