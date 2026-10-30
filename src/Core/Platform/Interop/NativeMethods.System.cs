using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    internal struct CpuLoad {
        public uint User, System, Idle, Nice;
    }
    [LibraryImport("libSystem.B.dylib", EntryPoint = "mach_host_self")]
    internal static partial uint MachHostSelf();
    [LibraryImport("libSystem.B.dylib", EntryPoint = "host_statistics")]
    internal static partial int HostCpuStatistics(uint host, int flavor, out CpuLoad statistics, ref uint count);
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);
}
