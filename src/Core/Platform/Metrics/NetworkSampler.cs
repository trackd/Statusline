using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using StatusLine.Interop;

namespace StatusLine.Metrics;

/// <summary>Aggregate receive/send throughput across active, non-loopback interfaces.</summary>
internal static class NetworkSampler {
    private static readonly Lock Gate = new();
    private static (long Received, long Sent, long Timestamp)? previous;

    public static (double ReceivedPerSecond, double SentPerSecond)? Sample() {
        lock (Gate) {
            return SampleCore();
        }
    }

    private static (double ReceivedPerSecond, double SentPerSecond)? SampleCore() {
        long received = 0, sent = 0;
        try {
            if (OperatingSystem.IsWindows()) {
                if (WindowsNetworkReader.Read() is not { } totals) {
                    return null;
                }

                (received, sent) = totals;
            }
            else {
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                        continue;
                    }

                    IPInterfaceStatistics statistics = networkInterface.GetIPStatistics();
                    received += statistics.BytesReceived;
                    sent += statistics.BytesSent;
                }
            }
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException) {
            return null;
        }

        long now = Stopwatch.GetTimestamp();
        (long Received, long Sent, long Timestamp)? last = previous;
        previous = (received, sent, now);
        if (last is not { } before) {
            return null;
        }

        double seconds = Stopwatch.GetElapsedTime(before.Timestamp, now).TotalSeconds;
        if (seconds <= 0) {
            return null;
        }

        return (Math.Max(0, received - before.Received) / seconds, Math.Max(0, sent - before.Sent) / seconds);
    }

    /// <summary>Formats a byte rate with binary units.</summary>
    public static string FormatRate(double bytesPerSecond) => bytesPerSecond switch {
        >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond / (1024 * 1024):0.0}M"),
        >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond / 1024:0}K"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytesPerSecond:0}B"),
    };
}

