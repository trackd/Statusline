using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [LibraryImport("shlwapi.dll", EntryPoint = "PathCompactPathExW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PathCompactPathEx([Out] char[] destination, string source, uint characters, uint flags);
    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetShortPathName(string path, [Out] char[] destination, uint characters);
}
