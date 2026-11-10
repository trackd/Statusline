using System.Globalization;
using System.Text;
using StatusLine.Refreshing;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Project;

internal sealed class GitBlock : IRefreshingBlock {
    private static readonly RefreshCache<(GitRepository Repository, string Environment), string?> OriginCache = new(128);
    public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Git, StatusLineAlignment.Left, ("\U000F062C", "⎇", "git"), Facts: SessionFacts.Location);

    public TimeSpan Interval => TimeSpan.FromSeconds(2);
    public CollectionScope Scope(BlockContext context) => new(context.State.FileSystemPath ?? string.Empty, ProviderEnvironment.Fingerprint);
    public async Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
        if (context.State.FileSystemPath is not { } path ||
            GitRepository.Find(path) is not { } repository ||
            repository.ReadBranch() is not { } branch) {
            return null;
        }

        GitStatus? status = await GitStatusProvider.ReadStatusAsync(repository, token).ConfigureAwait(false);
        return new CollectedBlock(set => Format(branch, status, set),
            (presentation, content) => WithOriginIcon(repository, presentation, content));
    }

    private BlockContent? Format(string branch, GitStatus? status, IconSet set) {
        string icon = Metadata.Icon(set);
        if (status is null) {
            return (icon, branch, Metadata.Alignment, Green);
        }

        (string Ahead, string Behind, string Staged, string Modified, string Untracked, string Conflicts) = Counters(set);
        StringBuilder builder = StringBuilderCache.Rent(branch.Length + 64);
        builder.Append(branch);
        AppendCount(builder, Ahead, status.Ahead);
        AppendCount(builder, Behind, status.Behind);
        AppendCount(builder, Staged, status.Staged);
        AppendCount(builder, Modified, status.Modified);
        AppendCount(builder, Untracked, status.Untracked);
        AppendCount(builder, Conflicts, status.Conflicts);
        TerminalColor color = Green;
        if (status.Conflicts > 0) {
            color = Red;
        }
        else if (status.IsDirty) {
            color = Yellow;
        }

        return (icon, StringBuilderCache.Complete(builder), Metadata.Alignment, color);
    }

    private static BlockContent WithOriginIcon(GitRepository repository, BlockContext context, BlockContent content) {
        if (context.IconSet != IconSet.NerdFont ||
            OriginCache.Get((repository, ProviderEnvironment.Fingerprint)) is not { } entry) {
            return content;
        }

        string? host = entry.Read(repository, GitStatusProvider.ReadOriginHostAsync,
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(context.BlockTimeoutMilliseconds), context.SlowProviderWait).Value;
        return ProviderIcon(host) is { } icon ? content with { Icon = icon } : content;
    }

    private static string? ProviderIcon(string? host) => host?.ToLowerInvariant() switch {
        "github.com" or "ssh.github.com" => "\uF09B", // nf-fa-github
        "gitlab.com" or "altssh.gitlab.com" => "\uF296", // nf-fa-gitlab
        "dev.azure.com" or "ssh.dev.azure.com" => "\U000F0FD5", // nf-md-microsoft_azure_devops
        string name when name.EndsWith(".visualstudio.com", StringComparison.Ordinal) => "\U000F0FD5",
        _ => null,
    };

    private static void AppendCount(StringBuilder builder, string symbol, int count) {
        if (count > 0) {
            builder.Append(' ').
            Append(symbol).
            Append(count.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static (string Ahead, string Behind, string Staged, string Modified, string Untracked, string Conflicts) Counters(IconSet set) =>
        set switch {
            // nf-md: arrow_up_bold, arrow_down_bold, file_check, file_edit, file_question, source_merge.
            IconSet.NerdFont => ("\U000F0737", "\U000F072E", "\U000F0216 ", "\U000F11E7 ", "\U000F086F ", "\U000F062D "),
            IconSet.Unicode => ("⇡", "⇣", "+", "~", "?", "!"),
            _ => ("^", "v", "+", "~", "?", "!"),
        };
}
