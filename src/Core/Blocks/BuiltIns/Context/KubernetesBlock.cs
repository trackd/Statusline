
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Context;

internal sealed class KubernetesBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Kubernetes, StatusLineAlignment.Left, ("\U000F10FE", "K8s", "K8s"), Facts: SessionFacts.Location);

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
        string config = Environment.GetEnvironmentVariable("KUBECONFIG") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kube", "config");
        foreach (string file in config.Split(Path.PathSeparator)) {
            if (File.Exists(file)) {
                return ProjectReader.RunAsync("kubectl", ["config", "current-context"], path, token);
            }
        }

        return Task.FromResult<string?>(null);
    }
}
