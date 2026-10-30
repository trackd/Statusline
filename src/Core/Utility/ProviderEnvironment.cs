using System.Diagnostics;

namespace StatusLine.Utility;

internal static class ProviderEnvironment {
    private static readonly Lock Gate = new();
    private static readonly string[] Variables = ["PATH", "VIRTUAL_ENV", "CONDA_PREFIX", "CONDA_DEFAULT_ENV", "KUBECONFIG", "DOCKER_CONTEXT", "HOME", "USERPROFILE", "DOTNET_ROOT", "RUSTUP_TOOLCHAIN", "JAVA_HOME", "PYENV_VERSION", "RBENV_VERSION"];
    private static long updated;

    public static string Fingerprint {
        get {
            lock (Gate) {
                if (updated == 0 || Stopwatch.GetElapsedTime(updated).TotalSeconds >= 1) {
                    field = string.Join('\0', Variables.Select(Environment.GetEnvironmentVariable));
                    updated = Stopwatch.GetTimestamp();
                }

                return field;
            }
        }
    } = string.Empty;
}
