
namespace StatusLine.Blocks.Execution;

[Flags]
internal enum SessionFacts {
    None = 0,
    Errors = 1,
    Stack = 2,
    NestedPrompt = 4,
    Runspaces = 8,
    Location = 16,
    ExitCode = 32,
    All = Errors | Stack | NestedPrompt | Runspaces | Location | ExitCode,
}

internal sealed record BlockMetadata(StatusLineBlockKind Kind, StatusLineAlignment Alignment,
    (string NerdFont, string Unicode, string None) Icons, SessionFacts Facts = SessionFacts.None,
    bool OrdinaryLeadingSpace = false) {
    public string Icon(IconSet set) => set switch {
        IconSet.NerdFont => Icons.NerdFont,
        IconSet.Unicode => Icons.Unicode,
        _ => Icons.None,
    };
}

internal interface IBuiltInBlock {
    BlockMetadata Metadata { get; }
}

internal interface IImmediateBlock : IBuiltInBlock {
    BlockContent? Evaluate(BlockContext context);
}

internal interface IRefreshingBlock : IBuiltInBlock {
    TimeSpan Interval { get; }
    CollectionScope Scope(BlockContext context);
    Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token);
}

internal readonly record struct CollectionScope(string Path, string Environment);

// Acquisition occurs once. Icon-specific text is materialized only when requested and retained
// with the sample, so presentation changes never start a new process or read system metrics.
internal sealed class CollectedBlock(Func<IconSet, BlockContent?> present) {
    private readonly Lock gate = new();
    private readonly BlockContent?[] variants = new BlockContent?[3];
    private int initialized;

    public BlockContent? Present(IconSet icons) {
        int index = (int)icons;
        int bit = 1 << index;
        lock (gate) {
            if ((initialized & bit) == 0) {
                variants[index] = present(icons);
                initialized |= bit;
            }

            return variants[index];
        }
    }
}
