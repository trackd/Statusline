using System.Text;

namespace StatusLine.Cli;

// Returned memory is valid until the next field read. StreamReader retains UTF-8
// decoder state across byte fragments; only decoded characters are split on NUL.
internal sealed class ShellFieldReader(Stream source, int maximumFieldLength) : IDisposable {
    private readonly StreamReader reader = new(source, leaveOpen: true);
    private readonly char[] buffer = new char[4096];
    private StringBuilder? scratch;
    private int position;
    private int length;

    internal async ValueTask<ReadOnlyMemory<char>> ReadAsync(CancellationToken token) {
        StringBuilder? fragment = null;
        while (true) {
            token.ThrowIfCancellationRequested();
            if (position == length) {
                length = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                position = 0;
                if (length == 0) { throw new EndOfStreamException(); }
            }

            int separator = buffer.AsSpan(position, length - position).IndexOf('\0');
            int count = separator < 0 ? length - position : separator;
            if ((fragment?.Length ?? 0) + count > maximumFieldLength) { throw new IOException("Shell field exceeds 64 KiB."); }

            if (separator >= 0) {
                ReadOnlyMemory<char> field = buffer.AsMemory(position, count);
                position += count + 1;
                if (fragment is null) { return field; }

                fragment.Append(field.Span);
                return fragment.ToString().AsMemory();
            }

            if (fragment is null) {
                fragment = scratch ??= new StringBuilder();
                fragment.Clear();
            }

            fragment.Append(buffer.AsSpan(position, count));
            position = length;
        }
    }

    internal async ValueTask<string> ReadStringAsync(CancellationToken token) =>
        (await ReadAsync(token).ConfigureAwait(false)).ToString();

    internal async ValueTask<bool> MatchesAsync(string expected, CancellationToken token) =>
        (await ReadAsync(token).ConfigureAwait(false)).Span.SequenceEqual(expected);

    public void Dispose() => reader.Dispose();
}
