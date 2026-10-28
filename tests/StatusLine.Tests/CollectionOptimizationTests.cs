using System.Runtime.InteropServices;
using StatusLine.Interop;
using StatusLine.Metrics;
using StatusLine.Platform;
using Xunit;

namespace StatusLine.Tests;

public sealed class CollectionOptimizationTests {
    [Fact]
    public void GpuKeysAreReusedWithoutRetainingPerProcessInstances() {
        var engines = new Dictionary<string, double>(StringComparer.Ordinal);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        WindowsGpuReader.AddEngine(engines, "pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 20, keys);
        string original = Assert.Single(engines.Keys);
        engines.Clear();
        WindowsGpuReader.AddEngine(engines, "pid_999_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 30, keys);
        Assert.Same(original, Assert.Single(engines.Keys));
        Assert.Single(keys);
        Assert.Equal(30, Assert.Single(engines.Values));
    }

    [Fact]
    public void NetworkIncludesOnlyCurrentUpNonLoopbackRowsAndUses64BitCounters() {
        NativeMethods.InterfaceRow2[] rows = [
            new() { OperationalStatus = 1, Type = 6, InOctets = 5_000_000_000, OutOctets = 7_000_000_000 },
            new() { OperationalStatus = 2, Type = 6, InOctets = 100, OutOctets = 200 },
            new() { OperationalStatus = 1, Type = 24, InOctets = 300, OutOctets = 400 },
        ];
        Assert.Equal((5_000_000_000L, 7_000_000_000L), WindowsNetworkReader.Sum(rows));
        rows[0].OperationalStatus = 2;
        rows[1].OperationalStatus = 1;
        Assert.Equal((100L, 200L), WindowsNetworkReader.Sum(rows));
        Assert.Equal((0L, 0L), WindowsNetworkReader.Sum([]));
    }

    [Fact]
    public void NativeNetworkTableMatchesWindowsSdkLayout() {
        Assert.Equal(1352, Marshal.SizeOf<NativeMethods.InterfaceRow2>());
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.InterfaceTable2>(nameof(NativeMethods.InterfaceTable2.First)).ToInt32());
        Assert.Equal(1156, Marshal.OffsetOf<NativeMethods.InterfaceRow2>(nameof(NativeMethods.InterfaceRow2.OperationalStatus)).ToInt32());
        Assert.Equal(1208, Marshal.OffsetOf<NativeMethods.InterfaceRow2>(nameof(NativeMethods.InterfaceRow2.InOctets)).ToInt32());
        Assert.Equal(1280, Marshal.OffsetOf<NativeMethods.InterfaceRow2>(nameof(NativeMethods.InterfaceRow2.OutOctets)).ToInt32());
    }

    [Fact]
    public void UserLookupReusesNameButInvalidatesOnIdentityAndExpiry() {
        int lookups = 0;
        var clock = new IdentityClock();
        var cache = new UserIdentity(() => (++lookups).ToString(System.Globalization.CultureInfo.InvariantCulture), clock);
        Assert.Equal("1", cache.Get(1000));
        clock.Timestamp = 29;
        Assert.Equal("1", cache.Get(1000));
        Assert.Equal("2", cache.Get(1001));
        clock.Timestamp = 60;
        Assert.Equal("3", cache.Get(1001));
        Assert.Equal(3, lookups);
    }

    private sealed class IdentityClock : TimeProvider {
        internal long Timestamp;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Timestamp;
    }
}
