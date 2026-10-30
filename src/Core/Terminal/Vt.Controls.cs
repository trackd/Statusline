namespace StatusLine;

// Named ECMA/DEC control bytes and complete fixed sequences shared by emitters and parsers.
internal static partial class Vt {
    internal const char Escape = '\e';
    internal const char BellCharacter = '\a';
    internal const char Cancel = '\u0018';
    internal const char Substitute = '\u001a';
    internal const char DeviceControlString8Bit = '\u0090';
    internal const char StartOfString8Bit = '\u0098';
    internal const char ControlSequence8Bit = '\u009b';
    internal const char StringTerminator8Bit = '\u009c';
    internal const char OperatingSystemCommand8Bit = '\u009d';
    internal const char PrivacyMessage8Bit = '\u009e';
    internal const char ApplicationCommand8Bit = '\u009f';
    internal const char StringTerminatorFinal = '\\';
    internal const int C1IntroducerOffset = 0x40;
    internal const int ResetAttributesCode = 0;
    internal const int ExtendedForegroundCode = 38;
    internal const int DefaultForegroundCode = 39;
    internal const int ExtendedBackgroundCode = 48;
    internal const int DefaultBackgroundCode = 49;
    internal const int ExtendedUnderlineColorCode = 58;
    internal const int RgbColorMode = 2;
    internal const int IndexedColorMode = 5;
    internal const int StandardForegroundFirst = 30;
    internal const int StandardForegroundLast = 37;
    internal const int BrightForegroundFirst = 90;
    internal const int BrightForegroundLast = 97;
    internal const string Csi = "\e[";
    internal const string Dcs = "\eP";
    internal const string StringTerminator = "\e\\";
    internal const string Bell = "\a";
    internal const string Reset = "\e[0m";
    internal const string Reverse = "\e[7m";
    internal const string ResetReverse = "\e[0;7m";
    internal const string RedForeground = "\e[31m";
    internal const string DefaultForeground = "\e[39m";
    internal const string SelectMainDisplay = "\e[0$}";
    internal const string SelectStatusDisplay = "\e[1$}";
    internal const string HostWritableStatusLine = "\e[2$~";
    internal const string ClearLine = "\e[2K";
    internal const string QueryStatusLineMode = "\eP$q$~\e\\";
    internal const string EnableReflow = "\e[?2028h";
    internal const string DisableReflow = "\e[?2028l";
    internal const string ClearProgress = "\e]9;4;0;50\e\\";
    internal const string IndeterminateProgress = "\e]9;4;3;50\e\\";
}
