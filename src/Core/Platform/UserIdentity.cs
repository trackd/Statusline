using System.Security.Principal;
using StatusLine.Interop;

namespace StatusLine.Platform;

/// <summary>Caches name-service lookup, while honoring thread impersonation and Unix UID changes.</summary>
internal sealed class UserIdentity(Func<string> readName, TimeProvider clock) {
    private static readonly UserIdentity Shared = new(static () => Environment.UserName, TimeProvider.System);
    private readonly Lock gate = new();
    private string? name;
    private uint identity;
    private long expires;

    internal static string CurrentName {
        get {
            if (OperatingSystem.IsWindows()) {
                using var impersonation = WindowsIdentity.GetCurrent(ifImpersonating: true);
                if (impersonation is not null) {
                    return Environment.UserName;
                }

                return Shared.Get(0);
            }

            return Shared.Get(NativeMethods.GetEffectiveUserId());
        }
    }

    internal string Get(uint currentIdentity) {
        lock (gate) {
            long now = clock.GetTimestamp();
            if (name is null || identity != currentIdentity || now >= expires) {
                name = readName();
                identity = currentIdentity;
                expires = now + (long)(clock.TimestampFrequency * TimeSpan.FromSeconds(30).TotalSeconds);
            }

            return name;
        }
    }
}
