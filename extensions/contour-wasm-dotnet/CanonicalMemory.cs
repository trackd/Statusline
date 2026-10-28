using System.Runtime.InteropServices;

namespace StatuslineWorld;

internal static unsafe class CanonicalMemory {
    // Generated bindings release incoming buffers with NativeMemory.Free.
    [UnmanagedCallersOnly(EntryPoint = "cabi_realloc")]
    public static nint Reallocate(nint pointer, int oldSize, int alignment, int newSize) {
        if (newSize == 0) {
            NativeMemory.Free((void*)pointer);
            return 0;
        }
        // This world's canonical values require alignment of at most eight bytes.
        if (newSize < 0 || alignment is not (1 or 2 or 4 or 8)) {
            Environment.FailFast("Invalid canonical allocation.");
        }
        return (nint)NativeMemory.Realloc((void*)pointer, (nuint)newSize);
    }
}
