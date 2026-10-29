using System.Diagnostics;
using System.Globalization;

namespace StatusLine.Cli.Tests;

internal static partial class Program {
    private static void WorkerStartupStreams() {
        using var fixture = new Fixture();
        fixture.Configure(new { Left = new[] { "Status" } });
        var start = new ProcessStartInfo(binary) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "start", "--parent-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--config", fixture.Config }) {
            start.ArgumentList.Add(argument);
        }
        using Process launcher = Process.Start(start) ?? throw new IOException("Worker launcher did not start.");
        byte[] tokenBuffer = new byte[32];
        launcher.StandardOutput.BaseStream.ReadExactlyAsync(tokenBuffer.AsMemory()).AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        string token = System.Text.Encoding.ASCII.GetString(tokenBuffer);
        fixture.Sessions.Add(token);
        Check(launcher.WaitForExit(5000) && launcher.ExitCode == 0, "Worker launcher did not exit successfully.");
        Task<string> output = launcher.StandardOutput.ReadToEndAsync();
        Task<string> error = launcher.StandardError.ReadToEndAsync();
        try { Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (TimeoutException exception) {
            throw new IOException("Worker inherited the exited launcher's output handles; its captured streams never reached EOF.", exception);
        }
        Check(output.Result.Length == 0 && error.Result.Length == 0, "Unexpected launcher output after its token.");
        Check(WorkerPid(token) > 0, "Closing launcher streams stopped the worker.");
    }
}
