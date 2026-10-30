using System.Text;

namespace StatusLine;

/// <summary>
/// Reusable storage for the last rendered frame. Frames are compared and written as spans,
/// so a steady-state redraw allocates nothing.
/// </summary>
internal sealed class FrameBuffer {
    private char[] chars = [];
    private int length = -1;

    internal ReadOnlySpan<char> Text => chars.AsSpan(0, Math.Max(0, length));

    internal void Invalidate() => length = -1;

    /// <summary>Copies <paramref name="source"/> when its content differs; returns whether it changed.</summary>
    internal bool Update(StringBuilder source) {
        int count = source.Length;
        if (count == length && source.Equals(Text)) {
            return false;
        }

        if (chars.Length < count) {
            chars = new char[Math.Max(count, chars.Length * 2)];
        }

        source.CopyTo(0, chars.AsSpan(0, count), count);
        length = count;
        return true;
    }

    public override string ToString() => new(Text);
}
