using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using StatusLine.Interop;

namespace StatusLine.Metrics;

/// <summary>System-wide CPU usage between successive calls; the first call reports the average since boot.</summary>
internal static class CpuSampler {
    private static readonly Lock Gate = new();
    private static (ulong Total, ulong Idle) previous;

    public static double? Sample() {
        lock (Gate) {
            if (ReadTimes() is not { } times) {
                return null;
            }

            if (times.Total < previous.Total || times.Idle < previous.Idle) {
                previous = times;
                return null;
            }

            ulong total = times.Total - previous.Total;
            ulong idle = times.Idle - previous.Idle;
            previous = times;
            return total == 0 ? null : 100.0 * (total - Math.Min(idle, total)) / total;
        }
    }

    private static class MacCpu {
        internal static readonly uint Host = NativeMethods.MachHostSelf();
    }

    private static (ulong Total, ulong Idle)? ReadTimes() {
        try {
            if (OperatingSystem.IsWindows()) {
                // Kernel time includes idle time.
                return NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user)
                    ? ((ulong)(kernel + user), (ulong)idle)
                    : null;
            }

            if (OperatingSystem.IsMacOS()) {
                uint count = 4;
                if (NativeMethods.HostCpuStatistics(MacCpu.Host, 3, out NativeMethods.CpuLoad load, ref count) != 0 || count < 4) {
                    return null;
                }

                return ((ulong)load.User + load.System + load.Idle + load.Nice, load.Idle);
            }

            if (OperatingSystem.IsLinux()) {
                using var reader = new StreamReader("/proc/stat");
                string[]? fields = reader.ReadLine()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields is null || fields.Length < 5 || fields[0] != "cpu") {
                    return null;
                }

                ulong total = 0;
                // guest/guest_nice are already included in user/nice.
                for (int index = 1; index < Math.Min(fields.Length, 9); index++) {
                    total += ulong.Parse(fields[index], CultureInfo.InvariantCulture);
                }

                ulong idleTime = ulong.Parse(fields[4], CultureInfo.InvariantCulture);
                if (fields.Length > 5) {
                    idleTime += ulong.Parse(fields[5], CultureInfo.InvariantCulture);
                }

                return (total, idleTime);
            }
        }
        catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception) {
        }

        return null;
    }
}

