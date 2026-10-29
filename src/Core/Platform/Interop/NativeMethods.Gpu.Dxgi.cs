using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct AdapterDescription {
        public fixed char Name[128];
        public uint Vendor, Device, Subsystem, Revision;
        public nuint VideoMemory, SystemMemory, SharedMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid id, out nint factory);

    // DXGI enumerates graphics adapters, excluding compute-only devices registered as GPU Engine
    // performance counters. Keep COM ABI traversal beside the native declarations.
    internal static unsafe Dictionary<string, string> GpuAdapterNames() {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Guid id = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(in id, out nint factory) < 0) {
            return names;
        }

        try {
            void** methods = *(void***)factory;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)methods[12];
            for (uint index = 0; index < 128; index++) {
                nint adapter = 0;
                if (enumerate(factory, index, &adapter) < 0) {
                    break;
                }

                try {
                    void** adapterMethods = *(void***)adapter;
                    var describe = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)adapterMethods[10];
                    AdapterDescription description = default;
                    if (describe(adapter, &description) >= 0 && (description.Flags & 2) == 0) {
                        string key = FormattableString.Invariant($"luid_0x{description.LuidHigh:x8}_0x{description.LuidLow:x8}");
                        names[key] = new string(description.Name);
                    }
                }
                finally {
                    ReleaseCom(adapter);
                }
            }
        }
        finally {
            ReleaseCom(factory);
        }

        return names;
    }

    private static unsafe void ReleaseCom(nint value) {
        if (value != 0) {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)value)[2];
            _ = release(value);
        }
    }
}
