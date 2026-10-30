
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Runtime;

internal sealed class NodeBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Node, StatusLineAlignment.Left, ("\uE718", "N", "node"), Facts: SessionFacts.Location);

    public TimeSpan Interval => TimeSpan.FromSeconds(10);
    public CollectionScope Scope(BlockContext context) => new(context.State.FileSystemPath ?? string.Empty, ProviderEnvironment.Fingerprint);
    public async Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        string? text = await ReadAsync(context.State.FileSystemPath ?? string.Empty, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        return new CollectedBlock(set => (Metadata.Icon(set), text, Metadata.Alignment, Cyan));
    }

    private static Task<string?> ReadAsync(string path, CancellationToken token) {
        (string program, string[] arguments, string[] markers, string[] patterns) = ("node", ["--version"], ["package.json", ".node-version"], ["*.js", "*.ts"]);
        if (!ProjectReader.HasProject(path, markers, patterns)) {
            return Task.FromResult<string?>(null);
        }

        return ProjectReader.RunAsync(program, arguments, path, token);
    }
}
