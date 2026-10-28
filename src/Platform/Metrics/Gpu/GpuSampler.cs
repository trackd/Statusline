using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace StatusLine.Metrics;

internal readonly record struct GpuReading(string Device, double Utilization, string? Name = null);

internal interface IGpuReader : IDisposable {
    GpuReading[] Read();
}

/// <summary>Lazy, process-shared collection; no GPU work runs unless the block is selected.</summary>
internal sealed class GpuSampler(Func<IGpuReader?> createReader) {
    internal static GpuSampler Shared { get; } = new(CreateReader);
    private readonly Lock gate = new();
    private IGpuReader? reader;
    private long sampled;
    private long attempted;
    private int cleanupQueued;
    private GpuReading[] cached = [];

    internal GpuReading[] Sample() {
        lock (gate) {
            if (sampled != 0 && Stopwatch.GetElapsedTime(sampled).TotalSeconds < 1) {
                return cached;
            }

            try {
                if (reader is null && (attempted == 0 || Stopwatch.GetElapsedTime(attempted).TotalMinutes >= 1)) {
                    attempted = Stopwatch.GetTimestamp();
                    reader = createReader();
                }

                cached = reader?.Read() ?? [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) {
                reader?.Dispose();
                reader = null;
                attempted = Stopwatch.GetTimestamp();
                cached = [];
            }

            sampled = Stopwatch.GetTimestamp();
            return cached;
        }
    }

    internal void Reset() {
        // A native discovery/read cannot necessarily be cancelled. Never block module removal
        // behind it. Queue at most one cleanup, which waits outside the foreground runspace.
        if (!gate.TryEnter()) {
            if (Interlocked.Exchange(ref cleanupQueued, 1) == 0) {
                ThreadPool.QueueUserWorkItem(static sampler => {
                    lock (sampler.gate) {
                        try {
                            sampler.ResetCore();
                        }
                        finally {
                            Volatile.Write(ref sampler.cleanupQueued, 0);
                        }
                    }
                }, this, preferLocal: false);
            }

            return;
        }

        try {
            ResetCore();
        }
        finally {
            gate.Exit();
        }
    }

    private void ResetCore() {
        reader?.Dispose();
        reader = null;
        cached = [];
        sampled = attempted = 0;
    }

    private static IGpuReader? CreateReader() {
        if (OperatingSystem.IsWindows()) {
            return WindowsGpuReader.Create();
        }

        if (OperatingSystem.IsLinux()) {
            return new LinuxGpuReader();
        }

        if (OperatingSystem.IsMacOS()) {
            return MacGpuReader.Create();
        }

        return null;
    }

    internal static string Format(ReadOnlySpan<GpuReading> readings) {
        if (readings.Length == 1) {
            return string.Create(CultureInfo.InvariantCulture, $"{readings[0].Utilization:0}%");
        }

        var text = new StringBuilder();
        foreach (GpuReading reading in readings) {
            if (text.Length > 0) {
                text.Append(" | ");
            }

            text.Append(CultureInfo.InvariantCulture, $"{reading.Name ?? reading.Device} {reading.Utilization:0}%");
        }

        return text.ToString();
    }
}
