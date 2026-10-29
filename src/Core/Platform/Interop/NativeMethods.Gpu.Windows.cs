using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Explicit)]
    internal struct PdhValue {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct PdhItem {
        public nint Name;
        public PdhValue Value;
    }
    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
    internal static partial uint PdhOpenQuery(nint source, nuint user, out nint query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint PdhAddEnglishCounter(nint query, string path, nuint user, out nint counter);
    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCollectQueryData(nint query);
    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    internal static partial uint PdhCounterArray(nint counter, uint format, ref uint bytes, out uint count, nint buffer);
    [LibraryImport("pdh.dll")]
    internal static partial uint PdhCloseQuery(nint query);
}
