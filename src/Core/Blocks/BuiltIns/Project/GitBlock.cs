using System.Globalization;
using System.Text;

using static StatusLine.Blocks.Execution.BlockFormatting;

namespace StatusLine.Blocks.BuiltIns.Project;

internal sealed class GitBlock : IRefreshingBlock {
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
        return new CollectedBlock(set => Format(branch, status, set));
    }

    private BlockContent? Format(string branch, GitStatus? status, IconSet set) {
        if (status is null) {
            return (Metadata.Icon(set), branch, Metadata.Alignment, Green);
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

        return (Metadata.Icon(set), StringBuilderCache.Complete(builder), Metadata.Alignment, color);
    }

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
