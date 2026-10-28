using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry {
        public uint Size, Usage, Id;
        public nuint Heap;
        public uint Module, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }
    [LibraryImport("kernel32.dll")] internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);
    // Fixed inline string marshalling requires the runtime marshaller.
#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
#pragma warning restore SYSLIB1054
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool CloseHandle(nint handle);
}
