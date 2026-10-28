using System.Diagnostics;

namespace StatusLine.Utility;

internal static class ProcessRunner {
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = new Process { StartInfo = start };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(token);
        Task<string> error = process.StandardError.ReadToEndAsync(token);
        try {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        finally {
            if (!process.HasExited) {
                try {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }

            try {
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }
    }

    public static Task<(int ExitCode, string Output, string Error)> RunAsync(string program, string[] arguments, string path, CancellationToken token) {
        var start = new ProcessStartInfo(program);
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            start.Environment["DOTNET_NOLOGO"] = "1";
        }

        if (path.Length > 0) {
            start.WorkingDirectory = path;
        }

        foreach (string argument in arguments) {
            start.ArgumentList.Add(argument);
        }

        return RunAsync(start, token);
    }
}
