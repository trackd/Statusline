using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using StatusLine.Interop;

namespace StatusLine.Metrics;

internal static class SystemMemoryReader {
    private static readonly Lock Gate = new();
    private static long updated;
    private static (ulong Used, ulong Total)? cached;

    public static (ulong Used, ulong Total)? Read() {
        lock (Gate) {
            if (updated == 0 || Stopwatch.GetElapsedTime(updated).TotalSeconds >= 1) {
                cached = ReadCurrent();
                updated = Stopwatch.GetTimestamp();
            }

            return cached;
        }
    }

    private static (ulong Used, ulong Total)? ReadCurrent() {
        if (OperatingSystem.IsWindows()) {
            var status = new NativeMethods.MemoryStatus { Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatus>() };
            return NativeMethods.GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0
                ? (status.TotalPhysical - status.AvailablePhysical, status.TotalPhysical) : null;
        }

        if (OperatingSystem.IsMacOS()) {
            return MacMemory.Read();
        }

        if (!OperatingSystem.IsLinux()) {
            return null;
        }

        try {
            ulong total = 0, available = 0;
            using var reader = new StreamReader("/proc/meminfo");
            while (reader.ReadLine() is { } line) {
                ReadOnlySpan<char> value = line.AsSpan();
                if (value.StartsWith("MemTotal:", StringComparison.Ordinal)) {
                    total = ParseKiB(value[9..]);
                }
                else if (value.StartsWith("MemAvailable:", StringComparison.Ordinal)) {
                    available = ParseKiB(value[13..]);
                }
            }

            return total > 0 ? (total - Math.Min(total, available), total) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            return null;
        }
    }

    private static ulong ParseKiB(ReadOnlySpan<char> text) {
        text = text.Trim();
        int end = text.IndexOf(' ');
        if (end >= 0) {
            text = text[..end];
        }

        return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) ? value * 1024 : 0;
    }

    private static class MacMemory {
        private static readonly uint Host = NativeMethods.MachHostSelf();

        public static (ulong Used, ulong Total)? Read() {
            nuint length = sizeof(ulong);
            uint count = 15;
            if (NativeMethods.SysCtlByName("hw.memsize", out ulong total, ref length, IntPtr.Zero, 0) != 0 || total == 0 || NativeMethods.HostPageSize(Host, out nuint pageSize) != 0 || NativeMethods.HostStatistics(Host, 2, out NativeMethods.VmStatistics stats, ref count) != 0) {
                return null;
            }

            ulong available = ((ulong)stats.Free + stats.Inactive) * pageSize;
            return (total - Math.Min(total, available), total);
        }
    }
}
