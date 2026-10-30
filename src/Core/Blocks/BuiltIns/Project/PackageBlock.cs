
using System.Text.Json;
using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Project;

internal sealed class PackageBlock : IRefreshingBlock {
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Package, StatusLineAlignment.Left, ("\uF487", "pkg", "pkg"), Facts: SessionFacts.Location);

    public TimeSpan Interval => TimeSpan.FromSeconds(10);
    public CollectionScope Scope(BlockContext context) => new(context.State.FileSystemPath ?? string.Empty, ProviderEnvironment.Fingerprint);
    public async Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        string? text = await ReadAsync(context.State.FileSystemPath ?? string.Empty, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        return new CollectedBlock(set => (Metadata.Icon(set), text, Metadata.Alignment, Cyan));
    }

    private static async Task<string?> ReadAsync(string path, CancellationToken token) {
        string? project = ProjectReader.FindProject(path, ["package.json"], []);
        if (project is null) {
            return null;
        }

        try {
            using var json = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(project, "package.json"), token).ConfigureAwait(false));
            if (json.RootElement.TryGetProperty("version", out JsonElement version) &&
                version.ValueKind == JsonValueKind.String) {
                return version.GetString();
            }

            return null;
        }
        catch (JsonException exception) {
            throw new InvalidDataException("Invalid package metadata.", exception);
        }
    }
}
