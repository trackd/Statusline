
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Runtime;

internal sealed class JavaBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Java, StatusLineAlignment.Left, ("\uE738", "J", "java"), Facts: SessionFacts.Location);

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
        (string program, string[] arguments, string[] markers, string[] patterns) = ("java", ["-version"], ["pom.xml", "build.gradle", "build.gradle.kts"], ["*.java"]);
        if (!ProjectReader.HasProject(path, markers, patterns)) {
            return Task.FromResult<string?>(null);
        }

        return ProjectReader.RunAsync(program, arguments, path, token);
    }
}
