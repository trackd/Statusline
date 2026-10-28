using System.Runtime.InteropServices;
using System.Text;
using StatusLine.Interop;

namespace StatusLine.Metrics;

internal sealed class NvidiaGpuReader : IGpuReader {
    private nint library;
    private bool initialized;
    private NativeMethods.NvmlInitialize? shutdown;
    private NativeMethods.NvmlReadUtilization? utilization;
    private readonly List<(nint Handle, string Id)> devices = [];

    internal static unsafe NvidiaGpuReader? Create() {
        var reader = new NvidiaGpuReader();
        if (!NativeLibrary.TryLoad("libnvidia-ml.so.1", out reader.library)) {
            return null;
        }

        try {
            T Bind<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(reader.library, symbol));
            NativeMethods.NvmlInitialize initialize = Bind<NativeMethods.NvmlInitialize>("nvmlInit_v2");
            reader.shutdown = Bind<NativeMethods.NvmlInitialize>("nvmlShutdown");
            NativeMethods.NvmlDeviceCount count = Bind<NativeMethods.NvmlDeviceCount>("nvmlDeviceGetCount_v2");
            NativeMethods.NvmlDeviceHandle handle = Bind<NativeMethods.NvmlDeviceHandle>("nvmlDeviceGetHandleByIndex_v2");
            NativeMethods.NvmlReadUuid uuid = Bind<NativeMethods.NvmlReadUuid>("nvmlDeviceGetUUID");
            reader.utilization = Bind<NativeMethods.NvmlReadUtilization>("nvmlDeviceGetUtilizationRates");
            reader.initialized = initialize() == 0;
            if (reader.initialized && count(out uint length) == 0 && length <= 128) {
                byte* text = stackalloc byte[96];
                for (uint index = 0; index < length; index++) {
                    if (handle(index, out nint device) == 0 && uuid(device, text, 96) == 0) {
                        ReadOnlySpan<byte> bytes = new(text, 96);
                        int end = bytes.IndexOf((byte)0);
                        if (end > 0) {
                            reader.devices.Add((device, Encoding.UTF8.GetString(bytes[..end])));
                        }
                    }
                }

                if (reader.devices.Count > 0) {
                    return reader;
                }
            }
        }
        catch (EntryPointNotFoundException) {
            // Driver API versions vary; absence is a supported outcome.
        }

        reader.Dispose();
        return null;
    }

    public GpuReading[] Read() {
        var readings = new List<GpuReading>(devices.Count);
        foreach ((nint handle, string? id) in devices) {
            int status = utilization!(handle, out NativeMethods.NvmlUtilization value);
            if (status == 15) {
                throw new IOException("GPU driver reports a removed device.");
            }

            if (status == 0 && value.Gpu <= 100) {
                readings.Add(new(id, value.Gpu));
            }
        }

        return [.. readings];
    }

    public void Dispose() {
        if (initialized) {
            shutdown?.Invoke();
            initialized = false;
        }

        if (library != 0) {
            NativeLibrary.Free(library);
            library = 0;
        }
    }
}
