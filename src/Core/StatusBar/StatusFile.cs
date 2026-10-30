using System.Buffers;
using System.Text;

namespace StatusLine;

/// <summary>Replaces the status file atomically so a reader never sees a half-written line.</summary>
internal static class StatusFile {
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static TemporaryName? temporaryName;

    public static string? LastError { get; private set; }

    public static bool TryWrite(string path, ReadOnlySpan<char> text) => Write(path, text, null);

    // Prepare the file outside a host's state lock, then let the host conditionally
    // publish it under that lock. A superseded frame never replaces newer state.
    internal static bool TryWriteIfCurrent(string path, ReadOnlySpan<char> text, Func<string, string, bool> publish) =>
        Write(path, text, publish);

    private static bool Write(string path, ReadOnlySpan<char> text, Func<string, string, bool>? publish) {
        // One writer per process owns the status line, so a per-process temporary name is unique.
        string temporary = Temporary(path);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(Utf8.GetMaxByteCount(text.Length));
        try {
            int count = Utf8.GetBytes(text, bytes);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 0)) {
                stream.Write(bytes, 0, count);
            }

            if (publish is null) { File.Move(temporary, path, overwrite: true); }
            else if (!publish(temporary, path)) {
                TryDelete(temporary);
                return false;
            }

            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            LastError = exception.Message;
            TryDelete(temporary);
            return false;
        }
        finally {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static string Temporary(string path) {
        TemporaryName? cached = temporaryName;
        if (cached is not null && string.Equals(cached.Path, path, StringComparison.Ordinal)) {
            return cached.Temporary;
        }

        cached = new TemporaryName(path, $"{path}.tmp.{Environment.ProcessId}");
        temporaryName = cached;
        return cached.Temporary;
    }

    private static void TryDelete(string temporary) {
        try {
            File.Delete(temporary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed record TemporaryName(string Path, string Temporary);
}
