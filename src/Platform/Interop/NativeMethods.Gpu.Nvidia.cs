using System.Runtime.InteropServices;

namespace StatusLine.Interop;

internal static partial class NativeMethods {
    [StructLayout(LayoutKind.Sequential)]
    internal struct NvmlUtilization { public uint Gpu, Memory; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int NvmlInitialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int NvmlDeviceCount(out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int NvmlDeviceHandle(uint index, out nint device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int NvmlReadUtilization(nint device, out NvmlUtilization utilization);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate int NvmlReadUuid(nint device, byte* value, uint length);
}
