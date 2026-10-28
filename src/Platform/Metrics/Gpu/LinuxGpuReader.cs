using System.Diagnostics;
using System.Globalization;

namespace StatusLine.Metrics;

internal sealed class LinuxGpuReader : IGpuReader {
    private NvidiaGpuReader? nvidia;
    private string[] amdDevices = [];
    private long discovered;

    public GpuReading[] Read() {
        if (discovered == 0 || Stopwatch.GetElapsedTime(discovered).TotalMinutes >= 1) {
            nvidia?.Dispose();
            nvidia = NvidiaGpuReader.Create();
            amdDevices = DiscoverAmd("/sys/class/drm");
            discovered = Stopwatch.GetTimestamp();
        }

        var readings = new List<GpuReading>();
        if (nvidia is not null) {
            readings.AddRange(nvidia.Read());
        }

        foreach (string path in amdDevices) {
            try {
                if (ReadAmd(path) is { } reading) {
                    readings.Add(reading);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                // A missing or restricted adapter does not suppress the others.
            }
        }

        return [.. readings.OrderBy(reading => reading.Device, StringComparer.Ordinal)];
    }

    internal static string[] DiscoverAmd(string root) {
        if (!Directory.Exists(root)) {
            return [];
        }

        var paths = new List<string>();
        foreach (string card in Directory.EnumerateDirectories(root, "card*")) {
            ReadOnlySpan<char> suffix = Path.GetFileName(card).AsSpan(4);
            if (suffix.IsEmpty || !int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out _)) {
                continue;
            }

            string device = Path.Combine(card, "device");
            try {
                if (File.ReadAllText(Path.Combine(device, "vendor")).Trim().Equals("0x1002", StringComparison.OrdinalIgnoreCase)) {
                    paths.Add(device);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        return [.. paths];
    }

    internal static GpuReading? ReadAmd(string device) {
        ReadOnlySpan<char> text = File.ReadAllText(Path.Combine(device, "gpu_busy_percent")).AsSpan().Trim();
        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out uint value) || value > 100) {
            return null;
        }

        string id = new DirectoryInfo(device).ResolveLinkTarget(true)?.Name ?? Directory.GetParent(device)!.Name;
        return new("AMD-" + id, value);
    }

    public void Dispose() {
        nvidia?.Dispose();
        nvidia = null;
    }
}
