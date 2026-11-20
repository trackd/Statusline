using StatusLine.Blocks.Execution;
using StatusLine.Refreshing;
using Xunit;

namespace StatusLine.Tests;

public sealed class RefreshScopeTests {
    [Fact]
    public async Task ChangedScopeDiscardsInFlightResultAndRefreshesWithoutWaitingForInterval() {
        using var entry = new RefreshValue<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<string> Read(string line, CancellationToken token) {
            if (Interlocked.Increment(ref calls) == 1) { started.SetResult(); return release.Task; }
            return Task.FromResult(line);
        }
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        _ = entry.Read("old", Read, TimeSpan.FromHours(1), timeout, TimeSpan.Zero, scope: ("old", 3));
        await started.Task.WaitAsync(timeout);
        Assert.False(entry.Read("new", Read, TimeSpan.FromHours(1), timeout, TimeSpan.Zero, scope: ("new", 3)).HasValue);
        Assert.Equal(1, calls);
        release.SetResult("old");
        using var deadline = new CancellationTokenSource(timeout);
        while (entry.IsBusy) { await Task.Delay(5, deadline.Token); }
        Assert.False(entry.Snapshot().HasValue);
        var result = entry.Read("new", Read, TimeSpan.FromHours(1), timeout, timeout, scope: ("new", 3));
        Assert.Equal("new", result.Value);
        Assert.Equal(2, calls);
    }
    [Fact]
    public void GitDetailedSharesTheCompactGitCollectionCache() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var context = new BlockContext { State = new() { FileSystemPath = path } };
        _ = BuiltInRefresh.Evaluate(BlockCatalog.Get(StatusLineBlockKind.Git), context, out _);
        _ = BuiltInRefresh.Evaluate(BlockCatalog.Get(StatusLineBlockKind.GitDetailed), context, out _);
        Assert.Equal("Git", Assert.Single(BuiltInRefresh.Diagnostics().Where(item => item.Path == path)).Name);
    }
}
