using System.Runtime.CompilerServices;
namespace StatusLine;

public static partial class Unicode {

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint UcdGraphemeJoins(uint state, int lead, int trail) {
        int l = lead & 15;
        int t = trail & 15;
        return (s_joinRules[state][l] >> (t * 2)) & 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool UcdGraphemeDone(uint state) => state == 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int UcdToCharacterWidth(int val) => val >> 6;

    /// <param name="str"></param>
    /// <param name="offset"></param>
    /// <param name="cp"></param>
    /// <returns>int</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Utf16NextOrFFFD(ReadOnlySpan<char> str, int offset, out uint cp) {
        uint c = str[offset];
        offset++;

        // Is any surrogate?
        if ((c & 0xF800) == 0xD800) {
            uint c1 = c;
            c = 0xfffd;

            // Is leading surrogate and not at end?
            if ((c1 & 0x400) == 0 && offset < str.Length) {
                char c2 = str[offset];
                // Is also trailing surrogate!
                if ((c2 & 0xFC00) == 0xDC00) {
                    c = (c1 << 10) - 0x35FDC00 + c2;
                    offset++;
                }
            }
        }

        cp = c;
        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TrySkipVTForward(ReadOnlySpan<char> str, ref int offset) {
        if (offset >= str.Length || !TerminalSequence.IsEscape(str[offset])) {
            return false;
        }

        offset += TerminalSequence.Length(str[offset..], out _);
        return true;
    }
}
