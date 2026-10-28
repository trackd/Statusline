namespace StatusLine;

// Shared VT grammar; callers decide whether to discard, neutralize, or retain a sequence.
internal static class TerminalSequence {
    internal static bool IsEscape(char character)
        => character is Vt.Escape or Vt.DeviceControlString8Bit or Vt.StartOfString8Bit or Vt.ControlSequence8Bit or Vt.OperatingSystemCommand8Bit or Vt.PrivacyMessage8Bit or Vt.ApplicationCommand8Bit;

    internal static int Length(ReadOnlySpan<char> value, out bool sgr) {
        sgr = false;
        bool c1 = value[0] != Vt.Escape;
        if (!c1 && value.Length < 2) {
            return 1;
        }

        char introducer = c1 ? (char)(value[0] - Vt.C1IntroducerOffset) : value[1];
        int start = c1 ? 1 : 2;
        if (introducer == '[') {
            for (int index = start; index < value.Length; index++) {
                if (value[index] is Vt.Escape or Vt.Cancel or Vt.Substitute) {
                    return index;
                }

                if (value[index] is >= '@' and <= '~') {
                    // Semicolon-only SGR; colon subparameters are neutralized when blocks are sanitized.
                    sgr = value[index] == 'm';
                    for (int parameter = start; sgr && parameter < index; parameter++) {
                        sgr = value[parameter] is (>= '0' and <= '9') or ';';
                    }

                    return index + 1;
                }
            }

            return value.Length;
        }

        if (introducer is ']' or 'P' or '_' or '^' or 'X') {
            for (int index = start; index < value.Length; index++) {
                if (value[index] is Vt.StringTerminator8Bit or Vt.Cancel or Vt.Substitute || (introducer == ']' && value[index] == Vt.BellCharacter)) {
                    return index + 1;
                }

                if (value[index] == Vt.Escape && index + 1 < value.Length && value[index + 1] == Vt.StringTerminatorFinal) {
                    return index + 2;
                }

                if (value[index] == Vt.Escape) {
                    return index;
                }
            }

            return value.Length;
        }

        if (c1) {
            return 1;
        }

        int final = 1;
        while (final < value.Length && value[final] is >= ' ' and <= '/') {
            final++;
        }

        return final < value.Length && value[final] is >= '0' and <= '~' ? final + 1 : Math.Max(1, final);
    }
}
