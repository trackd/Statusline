namespace StatusLine.Blocks.Execution;

/// <summary>A requested block: a built-in kind or a reusable block definition.</summary>
internal readonly record struct BlockSelector(StatusLineBlockKind? Kind, StatusLineBlockDefinition? Definition,
    StatusLineAlignment? Alignment = null, StatusLineBlock? Value = null) {
    internal IBuiltInBlock? Implementation { get; } = (Kind ?? Definition?.Kind) is { } kind
        ? BlockCatalog.Get(kind) : null;
    internal BlockBinding Binding { get; } = new();
    internal StatusLineAlignment EffectiveAlignment => Alignment ?? Definition?.EffectiveAlignment ??
        Value?.Alignment ?? Implementation?.Metadata.Alignment ?? StatusLineAlignment.Left;
    internal static SessionFacts RequiredFacts(params BlockSelector[][] groups) {
        SessionFacts required = SessionFacts.None;
        foreach (BlockSelector[] group in groups) {
            foreach (BlockSelector selector in group) {
                if (selector.Definition is { Kind: null }) {
                    return SessionFacts.All;
                }

                required |= selector.Implementation?.Metadata.Facts ?? SessionFacts.None;
            }
        }

        return required;
    }
    public static BlockSelector[] Parse(IEnumerable<object> values) => [.. values.Select(value => {
        object item = value is System.Management.Automation.PSObject wrapped ? wrapped.BaseObject : value;
        return item switch {
            StatusLineBlockDefinition definition => new BlockSelector(null, definition),
            StatusLineBlock block => new BlockSelector(null, null, Value: block),
            StatusLineBlockKind kind when Enum.IsDefined(kind) => new BlockSelector(kind, null),
            string name when Enum.TryParse(name, true, out StatusLineBlockKind kind) && Enum.IsDefined(kind) => new BlockSelector(kind, null),
            _ => throw new ArgumentException("Expected a built-in kind name or a block returned by New-StatusLineBlock."),
        };
    })];
}
