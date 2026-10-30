using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using StatusLine.Interop;

namespace StatusLine.Metrics;

/// <summary>Battery charge level and charging state.</summary>
internal static class BatteryReader {
    public static (int Percent, bool Charging)? Read() {
        try {
            if (OperatingSystem.IsWindows()) {
                if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SystemPowerStatus status) ||
                    status.BatteryFlag == 128 || status.BatteryFlag == 255 || status.BatteryLifePercent > 100) {
                    return null;
                }

                return (status.BatteryLifePercent, (status.BatteryFlag & 8) != 0);
            }

            if (OperatingSystem.IsMacOS()) {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                (int exitCode, string output, _) = ProcessRunner.RunAsync("/usr/bin/pmset", ["-g", "batt"], string.Empty, cancellation.Token).GetAwaiter().GetResult();
                int percentEnd = output.IndexOf('%');
                if (exitCode != 0 || percentEnd < 0) {
                    return null;
                }

                int start = percentEnd;
                while (start > 0 && char.IsAsciiDigit(output[start - 1])) {
                    start--;
                }

                if (int.TryParse(output.AsSpan(start, percentEnd - start), out int percent) && percent is >= 0 and <= 100) {
                    return (percent, output.Contains("; charging;", StringComparison.OrdinalIgnoreCase));
                }

                return null;
            }

            if (OperatingSystem.IsLinux() && Directory.Exists("/sys/class/power_supply")) {
                foreach (string supply in Directory.EnumerateDirectories("/sys/class/power_supply", "BAT*")) {
                    string capacityPath = Path.Combine(supply, "capacity");
                    if (!File.Exists(capacityPath)) {
                        continue;
                    }

                    int percent = int.Parse(File.ReadAllText(capacityPath).Trim(), CultureInfo.InvariantCulture);
                    string statusPath = Path.Combine(supply, "status");
                    bool charging = File.Exists(statusPath) && File.ReadAllText(statusPath).Trim() == "Charging";
                    return (percent, charging);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or FormatException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception) {
        }

        return null;
    }
}

