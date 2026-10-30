using System.Runtime.InteropServices;
using StatusLine.Interop;
using StatusLine.Metrics;
using Xunit;

namespace StatusLine.Tests;

public sealed class GpuTests {
    [Fact]
    public void DiscoveryIsLazyAndUnchangedSamplesReuseAcquisition() {
        int discoveries = 0;
        var reader = new FixtureReader();
        var sampler = new GpuSampler(() => {
            discoveries++;
            return reader;
        });
        Assert.Equal(0, discoveries);
        GpuReading[] first = sampler.Sample();
        Assert.Same(first, sampler.Sample());
        Assert.Equal(1, discoveries);
        Assert.Equal(1, reader.Reads);
        sampler.Reset();
        Assert.Equal(1, reader.Disposals);
    }

    [Fact]
    public void MissingNativeLibraryIsNegativelyCachedWithoutElevationOrRepeatedDiscovery() {
        int discoveries = 0;
        var sampler = new GpuSampler(() => {
            discoveries++;
            throw new DllNotFoundException();
        });
        Assert.Empty(sampler.Sample());
        Assert.Empty(sampler.Sample());
        Assert.Equal(1, discoveries);
    }

    [Fact]
    public async Task RemovalDoesNotWaitForBlockedNativeReadAndDisposesAfterCompletion() {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var reader = new FixtureReader {
            BeforeRead = () => {
                entered.Set();
                release.Wait();
            }
        };
        var sampler = new GpuSampler(() => reader);
        // Dedicated fixture threads keep unrelated blocking tests from starving this handshake.
        Task<GpuReading[]> pending = Task.Factory.StartNew(sampler.Sample,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            await Task.Factory.StartNew(sampler.Reset,
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, reader.Disposals);
        }
        finally {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }

        await reader.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, reader.Disposals);
    }

    private sealed class FixtureReader : IGpuReader {
        internal int Reads, Disposals;
        internal Action? BeforeRead;
        internal readonly TaskCompletionSource<bool> Disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GpuReading[] Read() {
            Reads++;
            BeforeRead?.Invoke();
            return [new("fixture", 42)];
        }
        public void Dispose() {
            Disposals++;
            Disposed.TrySetResult(true);
        }
    }

    [Fact]
    public void WindowsSumsProcessesOnTheSameEngineButDoesNotAddIndependentEngines() {
        var engines = new Dictionary<string, double>();
        WindowsGpuReader.AddEngine(engines, "pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 20);
        WindowsGpuReader.AddEngine(engines, "pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 30);
        WindowsGpuReader.AddEngine(engines, "pid_2_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 80);
        WindowsGpuReader.AddEngine(engines, "pid_3_luid_0x0_0x2_phys_0_eng_0_engtype_Compute", 0);
        GpuReading[] devices = WindowsGpuReader.FinishEngines(engines);
        Assert.Equal(2, devices.Length);
        Assert.Equal(80, devices[0].Utilization);
        Assert.Equal(0, devices[1].Utilization);
        Assert.NotEqual(devices[0].Device, devices[1].Device);
    }

    [Fact]
    public void WindowsIgnoresBadValuesAndClampsOneEngineAfterAggregation() {
        var engines = new Dictionary<string, double>();
        WindowsGpuReader.AddEngine(engines, "bad", 50);
        WindowsGpuReader.AddEngine(engines, "pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", double.NaN);
        WindowsGpuReader.AddEngine(engines, "pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", -1);
        Assert.Empty(engines);
        WindowsGpuReader.AddEngine(engines, "pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 75);
        WindowsGpuReader.AddEngine(engines, "pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 50);
        Assert.Equal(100, Assert.Single(WindowsGpuReader.FinishEngines(engines)).Utilization);
    }

    [Fact]
    public void WindowsExcludesNonGraphicsLuidAndKeepsAdapterNames() {
        var engines = new Dictionary<string, double> {
            ["luid_0x0_0x1_phys_0_eng_0"] = 20,
            ["luid_0x0_0x2_phys_0_eng_0"] = 99,
        };
        var adapters = new Dictionary<string, string> { ["luid_0x0_0x1"] = "Graphics adapter" };
        GpuReading gpu = Assert.Single(WindowsGpuReader.FinishEngines(engines, adapters));
        Assert.Equal(20, gpu.Utilization);
        Assert.Equal("Graphics adapter 0", gpu.Name);
    }

    [Theory]
    [InlineData(100, 100, 0)]
    [InlineData(100, 0, 100)]
    [InlineData(100, 75, 25)]
    public void MacReportsActiveResidencyWithoutFrequencyScaling(double total, double inactive, double expected) => Assert.Equal(expected, MacGpuReader.ActivePercent(total, inactive));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 101)]
    [InlineData(double.NaN, 0)]
    [InlineData(100, double.PositiveInfinity)]
    public void MacInvalidResidenciesAreUnavailableRatherThanIdle(double total, double inactive) => Assert.Null(MacGpuReader.ActivePercent(total, inactive));

    [Theory]
    [InlineData("OFF", true)]
    [InlineData("DOWN", true)]
    [InlineData("IDLE", true)]
    [InlineData("V0P0", false)]
    public void MacDistinguishesInactiveStates(string state, bool expected) => Assert.Equal(expected, MacGpuReader.IsInactive(state));

    [Fact]
    public void AmdDiscoveryAndReadsHandleAbsentAndInvalidMetrics() {
        string root = Path.Combine(Path.GetTempPath(), "statusline-gpu-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try {
            string device = Directory.CreateDirectory(Path.Combine(root, "card0", "device")).FullName;
            File.WriteAllText(Path.Combine(device, "vendor"), "0x1002\n");
            Directory.CreateDirectory(Path.Combine(root, "card0-DP-1"));
            Directory.CreateDirectory(Path.Combine(root, "card1", "device"));
            Assert.Equal(device, Assert.Single(LinuxGpuReader.DiscoverAmd(root)));
            foreach (string invalid in new[] { "", "bad", "101", "-1" }) {
                File.WriteAllText(Path.Combine(device, "gpu_busy_percent"), invalid);
                Assert.Null(LinuxGpuReader.ReadAmd(device));
            }

            File.WriteAllText(Path.Combine(device, "gpu_busy_percent"), "0\n");
            Assert.Equal(0, LinuxGpuReader.ReadAmd(device)!.Value.Utilization);
            File.WriteAllText(Path.Combine(device, "gpu_busy_percent"), "83\n");
            Assert.Equal(83, LinuxGpuReader.ReadAmd(device)!.Value.Utilization);
            Assert.Empty(LinuxGpuReader.DiscoverAmd(Path.Combine(root, "missing")));
        }
        finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PdhInteropPreservesTheNativeUnionAlignment() {
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.PdhValue>());
        Assert.Equal(8, Marshal.OffsetOf<NativeMethods.PdhValue>(nameof(NativeMethods.PdhValue.Value)).ToInt32());
        if (IntPtr.Size == 8) {
            Assert.Equal(24, Marshal.SizeOf<NativeMethods.PdhItem>());
        }
    }

    [Fact]
    public void PresentationKeepsIdleValuesAndIdentifiesMultipleDevices() {
        Assert.Equal("0%", GpuSampler.Format([new("adapter", 0)]));
        Assert.Equal("a 10% | b 20%", GpuSampler.Format([new("a", 10), new("b", 20)]));
        Assert.Empty(GpuSampler.Format([]));
    }
}
