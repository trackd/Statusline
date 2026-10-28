using System.Runtime.InteropServices;
using System.Text;
using StatusLine.Interop;

namespace StatusLine.Metrics;

/// <summary>Apple Silicon active GPU residency; no SMC, powermetrics, root or frequency tables.</summary>
internal sealed class MacGpuReader : IGpuReader {
    private const uint Utf8 = 0x08000100;
    private nint subscription;
    private nint channels;
    private nint previous;
    private nint channelsKey;

    internal static MacGpuReader? Create() {
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) {
            return null;
        }

        var reader = new MacGpuReader();
        bool ready = false;
        nint group = 0, subgroup = 0, source = 0;
        try {
            group = NativeMethods.CFStringCreateWithCString(0, "GPU Stats", Utf8);
            subgroup = NativeMethods.CFStringCreateWithCString(0, "GPU Performance States", Utf8);
            reader.channelsKey = NativeMethods.CFStringCreateWithCString(0, "IOReportChannels", Utf8);
            if (group == 0 || subgroup == 0 || reader.channelsKey == 0) {
                return null;
            }

            source = NativeMethods.IOReportCopyChannelsInGroup(group, subgroup, 0, 0, 0);
            if (source == 0) {
                return null;
            }

            nint array = NativeMethods.CFDictionaryGetValue(source, reader.channelsKey);
            if (array == 0 || NativeMethods.CFArrayGetCount(array) == 0) {
                return null;
            }

            reader.subscription = NativeMethods.IOReportCreateSubscription(0, source, out reader.channels, 0, 0);
            ready = reader.subscription != 0 && reader.channels != 0;
            if (ready) {
                return reader;
            }

            return null;
        }
        finally {
            Release(source);
            Release(subgroup);
            Release(group);
            if (!ready) {
                reader.Dispose();
            }
        }
    }

    public GpuReading[] Read() {
        nint current = NativeMethods.IOReportCreateSamples(subscription, channels, 0);
        if (current == 0) {
            return [];
        }

        nint delta = 0;
        try {
            if (previous == 0) {
                return [];
            }

            delta = NativeMethods.IOReportCreateSamplesDelta(previous, current, 0);
            if (delta == 0) {
                return [];
            }

            nint array = NativeMethods.CFDictionaryGetValue(delta, channelsKey);
            if (array == 0) {
                return [];
            }

            var readings = new List<GpuReading>();
            nint count = NativeMethods.CFArrayGetCount(array);
            if (count is < 0 or > 1024) {
                return [];
            }

            for (nint index = 0; index < count; index++) {
                nint channel = NativeMethods.CFArrayGetValueAtIndex(array, index);
                string? name = ReadString(NativeMethods.IOReportChannelGetChannelName(channel));
                if (name != "GPUPH") {
                    continue;
                }

                int states = NativeMethods.IOReportStateGetCount(channel);
                if (states is <= 0 or > 4096) {
                    continue;
                }

                double total = 0, inactive = 0;
                bool valid = true;
                for (int state = 0; state < states; state++) {
                    string? stateName = ReadString(NativeMethods.IOReportStateGetNameForIndex(channel, state));
                    long ticks = NativeMethods.IOReportStateGetResidency(channel, state);
                    if (stateName is null || ticks < 0) {
                        valid = false;
                        break;
                    }

                    total += ticks;
                    if (IsInactive(stateName)) {
                        inactive += ticks;
                    }
                }

                if (valid && ActivePercent(total, inactive) is { } percent) {
                    readings.Add(new("Apple-GPU", percent));
                }
            }

            return [.. readings];
        }
        finally {
            Release(delta);
            Release(previous);
            previous = current;
        }
    }

    internal static bool IsInactive(string state) => state is "OFF" or "DOWN" or "IDLE";

    internal static double? ActivePercent(double total, double inactive) {
        if (!double.IsFinite(total) || !double.IsFinite(inactive) || total <= 0 || inactive < 0 || inactive > total) {
            return null;
        }

        return 100 * (total - inactive) / total;
    }

    private static unsafe string? ReadString(nint value) {
        if (value == 0) {
            return null;
        }

        byte* buffer = stackalloc byte[256];
        if (NativeMethods.CFStringGetCString(value, buffer, 256, Utf8) == 0) {
            return null;
        }

        ReadOnlySpan<byte> text = new(buffer, 256);
        int end = text.IndexOf((byte)0);
        return end < 0 ? null : Encoding.UTF8.GetString(text[..end]);
    }

    private static void Release(nint value) {
        if (value != 0) {
            NativeMethods.CFRelease(value);
        }
    }

    public void Dispose() {
        Release(previous);
        Release(channels);
        Release(subscription);
        Release(channelsKey);
        previous = channels = subscription = channelsKey = 0;
    }
}
