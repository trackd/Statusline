using System.Diagnostics;
using System.Runtime.InteropServices;
using StatusLine.Interop;

namespace StatusLine.Metrics;

internal sealed class WindowsGpuReader : IGpuReader {
    private const uint DoubleFormat = 0x200;
    private const uint MoreData = 0x800007D2;
    private nint query;
    private nint counter;
    private nint buffer;
    private uint capacity;
    private bool baseline;
    private Dictionary<string, string> adapterNames = new(StringComparer.OrdinalIgnoreCase);
    private long discovered;
    private readonly Dictionary<string, double> engines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> engineKeys = new(StringComparer.Ordinal);

    internal static WindowsGpuReader? Create() {
        var reader = new WindowsGpuReader {
            adapterNames = NativeMethods.GpuAdapterNames(),
            discovered = Stopwatch.GetTimestamp()
        };
        if (NativeMethods.PdhOpenQuery(0, 0, out reader.query) == 0 &&
            NativeMethods.PdhAddEnglishCounter(reader.query, @"\GPU Engine(*)\Utilization Percentage", 0, out reader.counter) == 0) {
            return reader;
        }

        reader.Dispose();
        return null;
    }

    public unsafe GpuReading[] Read() {
        if (Stopwatch.GetElapsedTime(discovered).TotalMinutes >= 1) {
            adapterNames = NativeMethods.GpuAdapterNames();
            discovered = Stopwatch.GetTimestamp();
        }

        if (NativeMethods.PdhCollectQueryData(query) != 0) {
            baseline = false;
            return [];
        }

        if (!baseline) {
            baseline = true;
            return [];
        }

        for (int attempt = 0; attempt < 2; attempt++) {
            uint size = capacity;
            uint status = NativeMethods.PdhCounterArray(counter, DoubleFormat, ref size, out uint count, buffer);
            if (status == 0) {
                engines.Clear();
                if (engineKeys.Count > 2048) {
                    engineKeys.Clear();
                }

                int stride = sizeof(NativeMethods.PdhItem);
                if ((ulong)count * (uint)stride > capacity) {
                    return [];
                }

                for (int index = 0; index < count; index++) {
                    NativeMethods.PdhItem item = *(NativeMethods.PdhItem*)(buffer + (index * stride));
                    long offset = item.Name - buffer;
                    if (item.Value.Status is not (0 or 1) || offset < 0 || offset >= size || (offset & 1) != 0) {
                        continue;
                    }

                    ReadOnlySpan<char> name = new((void*)item.Name, checked((int)(size - offset) / sizeof(char)));
                    int end = name.IndexOf('\0');
                    if (end >= 0) {
                        AddEngine(engines, name[..end], item.Value.Value, engineKeys);
                    }
                }

                return FinishEngines(engines, adapterNames);
            }

            if (status != MoreData) {
                return [];
            }
            // Size returned for an undersized nonzero buffer is not reliable: query from zero.
            size = 0;
            if (NativeMethods.PdhCounterArray(counter, DoubleFormat, ref size, out _, 0) != MoreData || size > 16 * 1024 * 1024) {
                return [];
            }

            buffer = Marshal.ReAllocHGlobal(buffer, checked((nint)size));
            capacity = size;
        }

        return [];
    }

    internal static void AddEngine(Dictionary<string, double> engines, ReadOnlySpan<char> instance, double value,
        Dictionary<string, string>? keys = null) {
        int start = instance.IndexOf("luid_", StringComparison.Ordinal);
        int end = instance.IndexOf("_engtype_", StringComparison.Ordinal);
        if (start < 0 || end < start || !double.IsFinite(value) || value < 0) {
            return;
        }

        ReadOnlySpan<char> engine = instance[start..end];
        Dictionary<string, double>.AlternateLookup<ReadOnlySpan<char>> lookup = engines.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(engine, out double previous)) {
            lookup[engine] = previous + value;
            return;
        }

        string key;
        if (keys is not null) {
            Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> cached = keys.GetAlternateLookup<ReadOnlySpan<char>>();
            if (!cached.TryGetValue(engine, out string? existing)) {
                existing = engine.ToString();
                keys.Add(existing, existing);
            }

            key = existing;
        }
        else {
            key = engine.ToString();
        }

        engines.Add(key, value);
    }

    internal static GpuReading[] FinishEngines(Dictionary<string, double> engines, Dictionary<string, string>? adapterNames = null) {
        var devices = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((string? engine, double value) in engines) {
            int separator = engine.LastIndexOf("_eng_", StringComparison.Ordinal);
            if (separator < 0) {
                continue;
            }

            string device = engine[..separator];
            int physical = device.IndexOf("_phys_", StringComparison.Ordinal);
            if (adapterNames is not null && (physical < 0 || !adapterNames.ContainsKey(device[..physical]))) {
                continue;
            }

            devices[device] = Math.Max(devices.GetValueOrDefault(device), Math.Min(value, 100));
        }
        // Stable LUID/physical-device ordering; display labels are not adapter enumeration indices.
        return [.. devices.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => {
                string? name = null;
                int physical = pair.Key.IndexOf("_phys_", StringComparison.Ordinal);
                if (adapterNames is not null && physical >= 0) {
                    name = adapterNames[pair.Key[..physical]] + " " + pair.Key[(physical + 6)..];
                }

                return new GpuReading(pair.Key, pair.Value, name);
            })];
    }

    public void Dispose() {
        if (query != 0) {
            _ = NativeMethods.PdhCloseQuery(query);
            query = 0;
        }

        Marshal.FreeHGlobal(buffer);
        buffer = 0;
        capacity = 0;
    }
}
