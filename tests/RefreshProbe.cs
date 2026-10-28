using System;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Exercises the refresh state machine without depending on PowerShell runspace scheduling.
public static class RefreshProbe {
    public static async Task<string> CheckProcessAsync(Assembly assembly, string program, string marker) {
        var start = new ProcessStartInfo(program);
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("[IO.File]::WriteAllText('" + marker.Replace("'", "''") + "',[Environment]::ProcessId.ToString()); Start-Sleep -Seconds 30");
        Type runner = assembly.GetType("StatusLine.Utility.ProcessRunner", throwOnError: true);
        MethodInfo run = Array.Find(runner.GetMethods(), method => method.Name == "RunAsync" && method.GetParameters().Length == 2);
        using var timeout = new CancellationTokenSource(3000);
        bool cancelled = false;
        try { await (Task)run.Invoke(null, new object[] { start, timeout.Token }); }
        catch (OperationCanceledException) { cancelled = true; }
        if (!cancelled) return "Process did not observe cancellation";
        if (!File.Exists(marker)) return "Child did not start before cancellation";
        int id = int.Parse(File.ReadAllText(marker));
        for (int index = 0; index < 200; index++) {
            try {
                using var process = Process.GetProcessById(id);
                if (process.HasExited) return "OK";
            }
            catch (ArgumentException) { return "OK"; }
            await Task.Delay(5);
        }
        return "Child survived cancellation";
    }

}
