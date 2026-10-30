using StatusLine.Refreshing;

namespace StatusLine.Blocks.Execution;

internal static class BuiltInRefresh {
    private readonly record struct CollectionKey(StatusLineBlockKind Kind, CollectionScope Scope, int Timeout);
    private static readonly RefreshCache<CollectionKey, CollectedBlock?> Cache = new(256);

    public static BlockContent? Evaluate(IBuiltInBlock block, BlockContext context, out string? error) {
        error = null;
        if (block is IImmediateBlock immediate) {
            return immediate.Evaluate(context);
        }

        var refreshing = (IRefreshingBlock)block;
        var key = new CollectionKey(block.Metadata.Kind, refreshing.Scope(context), context.BlockTimeoutMilliseconds);
        RefreshValue<CollectedBlock?>? entry = Cache.Get(key);
        if (entry is null) {
            return null;
        }

        RefreshSnapshot<CollectedBlock?> snapshot = entry.Read((Block: refreshing, Context: context),
            static (request, token) => request.Block.CollectAsync(request.Context, token), refreshing.Interval,
            TimeSpan.FromMilliseconds(context.BlockTimeoutMilliseconds), context.SlowProviderWait);
        error = snapshot.Error;
        return snapshot.Value?.Present(context.IconSet);
    }

    public static IEnumerable<SegmentRefreshStatus> Diagnostics() {
        foreach ((CollectionKey key, RefreshValue<CollectedBlock?>? entry) in Cache.Inspect()) {
            RefreshSnapshot<CollectedBlock?> snapshot = entry.Snapshot();
            yield return new(key.Kind.ToString(), key.Scope.Path, snapshot.LastUpdated, snapshot.Error,
                snapshot.Error is not null, snapshot.IsBusy, entry.CollectionId, key.Timeout, null);
        }
    }
}
