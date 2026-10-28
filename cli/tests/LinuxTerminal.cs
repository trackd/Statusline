using System.Runtime.InteropServices;
using System.Text;

namespace StatusLine.Cli.Tests;

// Linux/glibc PTY fixture. Opaque spawn ABI sizes/flags follow Ubuntu's <spawn.h>;
// posix_spawn performs setsid/open/dup/exec without managed code after a fork.
internal sealed class LinuxTerminal : IDisposable {
    private readonly int master;
    private readonly int child;
    private readonly byte[] buffer = new byte[65536];
    private readonly StringBuilder transcript = new();

    internal LinuxTerminal(string shell, string directory, string termProgram = "") {
        if (Native.openpty(out master, out int slave, buffer, 0, 0) != 0) {
            throw new IOException("openpty failed.");
        }
        string slaveName = Encoding.UTF8.GetString(buffer.AsSpan(0, Array.IndexOf(buffer, (byte)0)));
        string[] arguments = shell switch {
            "bash" => ["bash", "--noprofile", "--norc", "-i"],
            "zsh" => ["zsh", "-f", "-i"],
            _ => ["fish", "--no-config", "--interactive"],
        };
        using var argv = new NativeStrings(arguments);
        var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
        environment["TERM"] = "xterm-256color";
        environment["TERM_PROGRAM"] = termProgram;
        environment["LC_ALL"] = "C.UTF-8";
        using var env = new NativeStrings(environment.Select(pair => pair.Key + "=" + pair.Value));
        Native.SpawnActions actions = default;
        Native.SpawnAttributes attributes = default;
        Check(Native.posix_spawn_file_actions_init(ref actions));
        Check(Native.posix_spawnattr_init(ref attributes));
        try {
            Check(Native.posix_spawnattr_setflags(ref attributes, 0x80)); // POSIX_SPAWN_SETSID
            Check(Native.posix_spawn_file_actions_addclose(ref actions, master));
            Check(Native.posix_spawn_file_actions_addclose(ref actions, slave));
            Check(Native.posix_spawn_file_actions_addopen(ref actions, 0, slaveName, 2, 0)); // O_RDWR
            Check(Native.posix_spawn_file_actions_adddup2(ref actions, 0, 1));
            Check(Native.posix_spawn_file_actions_adddup2(ref actions, 0, 2));
            Check(Native.posix_spawn_file_actions_addchdir_np(ref actions, directory));
            Check(Native.posix_spawnp(out child, shell, ref actions, ref attributes, argv.Pointer, env.Pointer));
        }
        finally {
            Native.posix_spawn_file_actions_destroy(ref actions);
            Native.posix_spawnattr_destroy(ref attributes);
            _ = Native.close(slave);
        }
        // Nonblocking master lets every wait have a fixed deadline.
        Check(Native.fcntl(master, 4, Native.fcntl(master, 3, 0) | 0x800));
        Resize(180);
        _ = ReadFor(300);
    }

    internal void Resize(ushort width) {
        var size = new Native.WindowSize { Rows = 30, Columns = width };
        if (Native.ioctl(master, 0x5414, ref size) != 0) { throw new IOException("PTY resize failed."); }
    }

    internal void Send(string text) {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (Native.write(master, bytes, (nuint)bytes.Length) != bytes.Length) { throw new IOException("PTY write failed."); }
    }

    internal string ReadFor(int milliseconds) {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using var bytes = new MemoryStream();
        while (elapsed.ElapsedMilliseconds < milliseconds) {
            nint count = Native.read(master, buffer, (nuint)buffer.Length);
            if (count > 0) {
                bytes.Write(buffer, 0, (int)count);
                string chunk = Encoding.UTF8.GetString(buffer, 0, (int)count);
                if (chunk.Contains("\x1b[6n", StringComparison.Ordinal)) { Send("\x1b[1;1R"); }
                if (chunk.Contains("\x1b[c", StringComparison.Ordinal) || chunk.Contains("\x1b[0c", StringComparison.Ordinal)) { Send("\x1b[?1;2c"); }
                if (chunk.Contains("\x1bP+q696e646e", StringComparison.Ordinal)) { Send("\x1bP0+r696e646e\x1b\\"); }
                if (chunk.Contains("\x1bP+q71756572792d6f732d6e616d65", StringComparison.Ordinal)) { Send("\x1bP0+r71756572792d6f732d6e616d65\x1b\\"); }
                if (chunk.Contains("\x1b[?u", StringComparison.Ordinal)) { Send("\x1b[?0u"); }
                if (chunk.Contains("\x1b[>0q", StringComparison.Ordinal)) { Send("\x1bP>|StatusLineTest(1.0)\x1b\\"); }
                if (chunk.Contains("\x1b]11;?", StringComparison.Ordinal)) { Send("\x1b]11;rgb:1111/1515/1c1c\x1b\\"); }
            }
            else { Thread.Sleep(5); }
        }
        string output = Encoding.UTF8.GetString(bytes.ToArray());
        transcript.Append(output);
        return output;
    }

    internal string Command(string text) {
        Send(text + "\n");
        var output = new StringBuilder();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < 10) {
            output.Append(ReadFor(30));
            if (output.ToString().Contains("__STATUSLINE_READY__", StringComparison.Ordinal)) { return output.ToString(); }
        }
        throw new IOException("PTY prompt timeout. Transcript: " + transcript);
    }

    public void Dispose() {
        _ = Native.kill(-child, 15);
        for (int index = 0; index < 50; index++) {
            if (Native.waitpid(child, out _, 1) == child) { _ = Native.close(master); return; }
            Thread.Sleep(10);
        }
        _ = Native.kill(-child, 9);
        Native.waitpid(child, out _, 0);
        _ = Native.close(master);
    }

    private static void Check(int code) {
        if (code < 0) { throw new IOException("PTY syscall failed: " + Marshal.GetLastPInvokeError()); }
        if (code > 0) { throw new IOException("PTY spawn error: " + code); }
    }

    private sealed class NativeStrings : IDisposable {
        private readonly nint[] strings;
        internal nint Pointer { get; }
        internal NativeStrings(IEnumerable<string> values) {
            strings = values.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            Pointer = Marshal.AllocHGlobal((strings.Length + 1) * IntPtr.Size);
            for (int index = 0; index < strings.Length; index++) { Marshal.WriteIntPtr(Pointer, index * IntPtr.Size, strings[index]); }
            Marshal.WriteIntPtr(Pointer, strings.Length * IntPtr.Size, 0);
        }
        public void Dispose() {
            foreach (nint text in strings) { Marshal.FreeCoTaskMem(text); }
            Marshal.FreeHGlobal(Pointer);
        }
    }
}

internal static partial class Native {
    [StructLayout(LayoutKind.Sequential, Size = 80)] internal struct SpawnActions;
    [StructLayout(LayoutKind.Sequential, Size = 336)] internal struct SpawnAttributes;
    [StructLayout(LayoutKind.Sequential)] internal struct WindowSize { internal ushort Rows, Columns, X, Y; }
    [LibraryImport("libc", SetLastError = true)] internal static partial int openpty(out int master, out int slave, [Out] byte[] name, nint term, nint size);
    [LibraryImport("libc")] internal static partial int posix_spawn_file_actions_init(ref SpawnActions actions);
    [LibraryImport("libc")] internal static partial int posix_spawn_file_actions_destroy(ref SpawnActions actions);
    [LibraryImport("libc")] internal static partial int posix_spawn_file_actions_addclose(ref SpawnActions actions, int fd);
    [LibraryImport("libc")] internal static partial int posix_spawn_file_actions_adddup2(ref SpawnActions actions, int fd, int target);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] internal static partial int posix_spawn_file_actions_addopen(ref SpawnActions actions, int fd, string path, int flags, uint mode);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] internal static partial int posix_spawn_file_actions_addchdir_np(ref SpawnActions actions, string path);
    [LibraryImport("libc")] internal static partial int posix_spawnattr_init(ref SpawnAttributes attributes);
    [LibraryImport("libc")] internal static partial int posix_spawnattr_destroy(ref SpawnAttributes attributes);
    [LibraryImport("libc")] internal static partial int posix_spawnattr_setflags(ref SpawnAttributes attributes, short flags);
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] internal static partial int posix_spawnp(out int pid, string file, ref SpawnActions actions, ref SpawnAttributes attributes, nint argv, nint envp);
    [LibraryImport("libc", SetLastError = true)] internal static partial int fcntl(int fd, int command, int value);
    [LibraryImport("libc", SetLastError = true)] internal static partial int ioctl(int fd, nuint request, ref WindowSize size);
    [LibraryImport("libc", SetLastError = true)] internal static partial nint read(int fd, [Out] byte[] data, nuint size);
    [LibraryImport("libc", SetLastError = true)] internal static partial nint write(int fd, byte[] data, nuint size);
    [LibraryImport("libc")] internal static partial int close(int fd);
    [LibraryImport("libc")] internal static partial int kill(int pid, int signal);
    [LibraryImport("libc")] internal static partial int waitpid(int pid, out int status, int options);
}
