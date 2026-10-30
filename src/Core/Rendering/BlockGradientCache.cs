using System.Drawing;

namespace StatusLine;

/// <summary>Reuses appearance-only copies for one compiled prompt or status group; only changed blocks are recolored.</summary>
internal sealed class BlockGradientCache(Color start, Color end, SegmentStyle style) {
    private readonly Lock gate = new();
    private StatusLineBlock[] previous = [];
    private StatusLineBlock[] colored = [];
    private bool unicode;

    internal IReadOnlyList<StatusLineBlock> Apply(IReadOnlyList<StatusLineBlock> blocks) {
        if (start.IsEmpty || blocks.Count == 0) {
            return blocks;
        }

        lock (gate) {
            bool reusable = previous.Length == blocks.Count && unicode == TextMeasurement.UseUnicode;
            int index = 0;
            if (reusable) {
                while (index < blocks.Count && ReferenceEquals(previous[index], blocks[index])) {
                    index++;
                }

                if (index == blocks.Count) {
                    return colored;
                }
            }
            // Callers may still hold the previous result, so a change publishes a new array.
            ReadOnlySpan<Color> colors = BlockGradient.Colors(start, end, blocks.Count);
            var next = new StatusLineBlock[blocks.Count];
            StatusLineBlock[] sources = reusable ? previous : new StatusLineBlock[blocks.Count];
            for (int position = 0; position < blocks.Count; position++) {
                StatusLineBlock block = blocks[position];
                next[position] = reusable && ReferenceEquals(previous[position], block)
                    ? colored[position]
                    : BlockGradient.Apply(block, colors[position], style);
                sources[position] = block;
            }

            previous = sources;
            colored = next;
            unicode = TextMeasurement.UseUnicode;
            return next;
        }
    }
}
