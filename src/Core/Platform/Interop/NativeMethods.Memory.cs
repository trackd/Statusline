using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatus {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [StructLayout(LayoutKind.Sequential)]
    internal struct VmStatistics {
        public uint Free, Active, Inactive, Wired, ZeroFill, Reactivations, PageIns, PageOuts, Faults, CopyOnWrite, Lookups, Hits, Purgeable, Purges, Speculative;
    }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int SysCtlByName(string name, out ulong value, ref nuint length, IntPtr newValue, nuint newLength);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "host_page_size")]
    internal static partial int HostPageSize(uint host, out nuint size);
    [LibraryImport("libSystem.B.dylib", EntryPoint = "host_statistics")]
    internal static partial int HostStatistics(uint host, int flavor, out VmStatistics statistics, ref uint count);
}
