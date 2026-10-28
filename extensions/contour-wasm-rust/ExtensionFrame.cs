using System;
using System.Buffers;
using System.Text;

namespace StatusLine.ContourPrototype;

/// <summary>OSC transport only: the WASM guest paints the decoded frame on its private surface.</summary>
public static class ExtensionFrame {
    private const string Prefix = "\u001b]1337;SetUserVar=statusline_frame=";
    private const int MaximumBytes = 64 * 1024;

    public static string CreateSequence(string text) {
        int byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount > MaximumBytes) {
            throw new ArgumentException("An extension frame cannot exceed 64 KiB of UTF-8.", nameof(text));
        }
        int encodedLength = (byteCount + 2) / 3 * 4;
        return string.Create(Prefix.Length + encodedLength + 2, (text, byteCount), static (destination, state) => {
            Prefix.AsSpan().CopyTo(destination);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(state.byteCount);
            try {
                int written = Encoding.UTF8.GetBytes(state.text.AsSpan(), buffer.AsSpan());
                Convert.TryToBase64Chars(buffer.AsSpan(0, written), destination[Prefix.Length..^2], out _);
                "\u001b\\".AsSpan().CopyTo(destination[^2..]);
            }
            finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        });
    }
}
