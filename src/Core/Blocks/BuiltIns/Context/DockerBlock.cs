
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Context;

internal sealed class DockerBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Docker, StatusLineAlignment.Left, ("\uE7B0", "Docker", "Docker"), Facts: SessionFacts.Location);

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
        if (Environment.GetEnvironmentVariable("DOCKER_CONTEXT") is { } context) {
            return Task.FromResult<string?>(context);
        }

        if (!ProjectReader.HasProject(path, ["Dockerfile", "compose.yaml", "docker-compose.yml"], [])) {
            return Task.FromResult<string?>(null);
        }

        return ProjectReader.RunAsync("docker", ["context", "show"], path, token);
    }
}
