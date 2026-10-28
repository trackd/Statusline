using System.Security.Principal;
using StatusLine.Interop;

namespace StatusLine.Platform;

internal static class Privilege {
    public static bool IsElevated { get; } = Detect();

    private static bool Detect() {
        if (OperatingSystem.IsWindows()) {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        return NativeMethods.GetEffectiveUserId() == 0;
    }
}
