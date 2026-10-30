using StatusLine.Blocks.Execution;
using StatusLine.Refreshing;
using Xunit;

namespace StatusLine.Tests;

public sealed class BlockEngineTests {
    [Fact]
    public void CatalogHasOneImplementationAndCapabilityPerKind() {
        var types = new HashSet<Type>();
        foreach (StatusLineBlockKind kind in Enum.GetValues<StatusLineBlockKind>()) {
            IBuiltInBlock block = BlockCatalog.Get(kind);
            Assert.Equal(kind, block.Metadata.Kind);
            Assert.True(types.Add(block.GetType()));
            Assert.True((block is IImmediateBlock) != (block is IRefreshingBlock));
            Assert.Same(block, BlockCatalog.Get(kind));
        }
    }

    [Fact]
    public void GitUsesMaterialDesignBranchAndCounters() {
        Assert.Equal(char.ConvertFromUtf32(0xF062C), BlockCatalog.Get(StatusLineBlockKind.Git).Metadata.Icon(IconSet.NerdFont));
        (string? Ahead, string? Behind, string? Staged, string? Modified, string? Untracked, string? Conflicts) = Blocks.BuiltIns.Project.GitBlock.Counters(IconSet.NerdFont);
        Assert.Equal(char.ConvertFromUtf32(0xF0737), Ahead);
        Assert.Equal(char.ConvertFromUtf32(0xF062D) + " ", Conflicts);
        Assert.Equal(("⇡", "⇣", "+", "~", "?", "!"), Blocks.BuiltIns.Project.GitBlock.Counters(IconSet.Unicode));
        Assert.Equal(("^", "v", "+", "~", "?", "!"), Blocks.BuiltIns.Project.GitBlock.Counters(IconSet.None));
    }

    [Fact]
    public void WrappedSelectionHasSameImplementationAndRequirements() {
        using var definition = new StatusLineBlockDefinition("errors", "", TimeSpan.Zero, StatusLineBlockKind.Errors);
        BlockSelector[] direct = BlockSelector.Parse(["Errors"]);
        BlockSelector[] wrapped = BlockSelector.Parse([definition]);
        Assert.Same(direct[0].Implementation, wrapped[0].Implementation);
        Assert.Equal(SessionFacts.Errors, BlockSelector.RequiredFacts(wrapped));
        Assert.Equal(SessionFacts.None, BlockSelector.RequiredFacts(BlockSelector.Parse(["Clock"])));
        Assert.Equal(SessionFacts.Location, BlockSelector.RequiredFacts(BlockSelector.Parse(["Directory", "Git"])));
        Assert.Equal(SessionFacts.ExitCode, BlockSelector.RequiredFacts(BlockSelector.Parse(["Status"])));
        using var script = new StatusLineBlockDefinition("script", "", TimeSpan.Zero);
        Assert.Equal(SessionFacts.All, BlockSelector.RequiredFacts(BlockSelector.Parse([script])));
    }

    [Fact]
    public void UnchangedContentReusesMeasuredOutputAndAppearanceEditInvalidatesIt() {
        BlockSelector[] selection = BlockSelector.Parse(["HistoryId"]);
        var context = new BlockContext { State = new() { HistoryId = 42 }, IconSet = IconSet.None };
        var appearance = new BlockOverrides(null, null, null, " ");
        StatusLineBlock first = Assert.Single(BlockEvaluator.Create(selection, context, appearance));
        Assert.Same(first, Assert.Single(BlockEvaluator.Create(selection, context, appearance)));
        StatusLineBlock changed = Assert.Single(BlockEvaluator.Create(selection, context, appearance with { Padding = ".." }));
        Assert.NotSame(first, changed);
        Assert.Equal("..", changed.Padding);
        Assert.Equal("42", changed.Text);
        Assert.Equal("43", Assert.Single(BlockEvaluator.Create(selection,
            context with { State = new() { HistoryId = 43 } }, appearance)).Text);
    }

    [Fact]
    public void CollectedPresentationIsLazyAndMemoizedPerIconMode() {
        int presentations = 0;
        var sample = new CollectedBlock(icons => {
            presentations++;
            return (icons.ToString(), "value", StatusLineAlignment.Left, default(TerminalColor));
        });
        Assert.Equal(0, presentations);
        Assert.Equal("None", sample.Present(IconSet.None)!.Value.Icon);
        sample.Present(IconSet.None);
        Assert.Equal(1, presentations);
        sample.Present(IconSet.Unicode);
        Assert.Equal(2, presentations);
    }

    [Fact]
    public void PromptRowsUseCompiledAlignmentForNamesEnumsAndDefinitions() {
        using var definition = new StatusLineBlockDefinition("clock", "", TimeSpan.Zero, StatusLineBlockKind.Clock);
        foreach (object selection in new object[] { "Clock", StatusLineBlockKind.Clock, definition }) {
            var configuration = new PromptConfiguration { Left = [], Right = [], Input = [], Block = [selection], Rows = 1 };
            var layout = new PromptLayout(configuration, elevated: false);
            Assert.Equal(2, layout.Rows);
        }

        var centered = new PromptLayout(new PromptConfiguration {
            Left = [],
            Right = [],
            Input = [],
            Block = ["PowerShell"],
            Rows = 1,
        }, elevated: false);
        Assert.Equal(2, centered.Rows);
    }

    [Fact]
    public void DefinitionEditsInvalidateBuiltInAndScriptPresentationWithoutRecollecting() {
        using var definition = new StatusLineBlockDefinition("history", "", TimeSpan.Zero, StatusLineBlockKind.HistoryId) {
            Foreground = new TerminalColor(System.Drawing.Color.Red),
            Bold = true,
        };
        BlockSelector[] selection = BlockSelector.Parse([definition]);
        var context = new BlockContext { State = new() { HistoryId = 42 } };
        var appearance = new BlockOverrides(null, null, null, " ");
        StatusLineBlock first = Assert.Single(BlockEvaluator.Create(selection, context, appearance));
        Assert.Same(first, Assert.Single(BlockEvaluator.Create(selection, context, appearance)));
        definition.Foreground = new TerminalColor(System.Drawing.Color.Blue);
        definition.Icon = "";
        definition.Bold = false;
        StatusLineBlock changed = Assert.Single(BlockEvaluator.Create(selection, context, appearance));
        Assert.NotSame(first, changed);
        Assert.Equal(new TerminalColor(System.Drawing.Color.Blue), changed.Foreground);
        Assert.Empty(changed.Icon);
        Assert.False(changed.Bold);
        Assert.Same(changed, Assert.Single(BlockEvaluator.Create(selection, context, appearance)));
        var layout = new PromptLayout(new PromptConfiguration {
            Left = [],
            Right = [],
            Input = [],
            Block = [definition],
            Rows = 1,
        }, elevated: false);
        Assert.Equal(1, layout.Rows);
        definition.Alignment = StatusLineAlignment.Right;
        Assert.Equal(2, layout.Rows);
        definition.Alignment = StatusLineAlignment.Left;
        Assert.Equal(1, layout.Rows);

        using var script = new StatusLineBlockDefinition("script", "", TimeSpan.Zero);
        var binding = new BlockBinding();
        List<object> output = ["constant"];
        StatusLineBlock[] initial = binding.RenderScript(output, script, appearance, null, null);
        script.Padding = "..";
        StatusLineBlock[] restyled = binding.RenderScript(output, script, appearance, null, null);
        Assert.NotSame(initial, restyled);
        Assert.Equal("..", Assert.Single(restyled).Padding);
        Assert.Same(restyled, binding.RenderScript(output, script, appearance, null, null));
    }

    [Fact]
    public void MutableScriptObjectsKeepTheirLiveTextConversion() {
        using var definition = new StatusLineBlockDefinition("mutable", "", TimeSpan.Zero);
        var value = new MutableText { Value = "first" };
        List<object> output = [value];
        var binding = new BlockBinding();
        var appearance = new BlockOverrides(null, null, null, " ");
        Assert.Equal("first", Assert.Single(binding.RenderScript(output, definition, appearance, null, null)).Text);
        value.Value = "second";
        Assert.Equal("second", Assert.Single(binding.RenderScript(output, definition, appearance, null, null)).Text);
    }

    private sealed class MutableText {
        internal string Value { get; set; } = "";
        public override string ToString() => Value;
    }

    [Fact]
    public void ExplicitFalseAndEmptyOverridesAreDifferentFromInheritance() {
        StatusLineBlock source = BlockSnapshots.Create("text", bold: true, icon: "source");
        using var inherit = new StatusLineBlockDefinition("inherit", "", TimeSpan.Zero);
        using var replace = new StatusLineBlockDefinition("replace", "", TimeSpan.Zero) {
            Bold = false,
            Icon = "",
            Foreground = default(TerminalColor),
        };
        var appearance = new BlockOverrides(null, null, null, " ");
        StatusLineBlock inherited = BlockEvaluator.RenderScriptItem(source, inherit.CaptureAppearance().Appearance, appearance, null, null);
        Assert.True(inherited.Bold);
        Assert.Equal("source", inherited.Icon);
        StatusLineBlock explicitValue = BlockEvaluator.RenderScriptItem(source, replace.CaptureAppearance().Appearance, appearance, null, null);
        Assert.False(explicitValue.Bold);
        Assert.Empty(explicitValue.Icon);
        Assert.True(explicitValue.Foreground.IsEmpty);
        Assert.True(explicitValue.PreserveForeground);
    }

    [Fact]
    public async Task FaultTextDoesNotTurnAnExceptionIntoADeadline() {
        using var entry = new RefreshValue<int>();
        entry.Get(_ => Task.FromException<int>(new IOException("Upstream timed out")),
            TimeSpan.FromHours(1), TimeSpan.FromSeconds(5), TimeSpan.Zero);
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        RefreshSnapshot<int> snapshot = entry.Snapshot();
        Assert.Equal(RefreshOutcome.Fault, snapshot.Outcome);
        Assert.False(snapshot.HasValue);
        Assert.Equal("Upstream timed out", snapshot.Error);
    }

    [Fact]
    public async Task NonCooperativeWorkKeepsItsWorkerUntilItActuallyExits() {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var entry = new RefreshValue<int>();
        Assert.Equal(7, entry.Get(_ => Task.FromResult(7), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        entry.Get(_ => release.Task, TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.Zero);
        await WaitUntil(() => entry.Snapshot().Outcome == RefreshOutcome.TimedOut);
        Assert.True(entry.Snapshot().IsBusy);
        Assert.Equal(7, entry.Snapshot().Value);
        release.SetResult(99);
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        Assert.Equal(7, entry.Snapshot().Value);
    }

    [Fact]
    public async Task SuccessfulRefreshFaultAndRecoveryKeepCoherentLastGoodState() {
        using var entry = new RefreshValue<int>();
        Assert.Equal(7, entry.Get(_ => Task.FromResult(7), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        RefreshSnapshot<int> initial = entry.Snapshot();
        Assert.Equal(RefreshOutcome.Success, initial.Outcome);
        Assert.NotNull(initial.LastUpdated);
        Assert.Null(initial.Error);
        Assert.Equal(7, entry.Get(_ => Task.FromException<int>(new IOException("broken")),
            TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        RefreshSnapshot<int> fault = entry.Snapshot();
        Assert.Equal(RefreshOutcome.Fault, fault.Outcome);
        Assert.Equal(initial.LastUpdated, fault.LastUpdated);
        Assert.Equal("broken", fault.Error);
        Assert.Equal(11, entry.Get(_ => Task.FromResult(11), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        await WaitUntil(() => !entry.Snapshot().IsBusy);
        Assert.Equal(RefreshOutcome.Success, entry.Snapshot().Outcome);
        Assert.Null(entry.Snapshot().Error);
    }

    [Fact]
    public async Task SynchronousCollectorIsSupervisedBeforeItsFirstAwait() {
        using var release = new ManualResetEventSlim();
        using var entry = new RefreshValue<int>();
        try {
            entry.Get(_ => {
                release.Wait(CancellationToken.None);
                return Task.FromResult(99);
            }, TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.Zero);
            await WaitUntil(() => entry.Snapshot().Outcome == RefreshOutcome.TimedOut);
            Assert.True(entry.Snapshot().IsBusy);
            Assert.False(entry.Snapshot().HasValue);
        }
        finally {
            release.Set();
            await WaitUntil(() => !entry.Snapshot().IsBusy);
        }

        Assert.False(entry.Snapshot().HasValue);
    }

    [Fact]
    public void ZeroCountsAreOmittedAndPositiveCountsAreVisible() {
        var block = (IImmediateBlock)BlockCatalog.Get(StatusLineBlockKind.Errors);
        var context = new BlockContext { IconSet = IconSet.None, State = new() { ErrorCount = 0 } };
        Assert.Null(block.Evaluate(context));
        BlockContent? result = block.Evaluate(context with { State = new() { ErrorCount = 2 } });
        Assert.Equal("2", result!.Value.Text);
        Assert.Equal("ERR", result.Value.Icon);
    }

    [Fact]
    public void IconChangesReuseAcquisitionButDifferentTimeoutPoliciesDoNot() {
        var block = new CountingBlock();
        var context = new BlockContext { IconSet = IconSet.None, SlowProviderWait = TimeSpan.FromSeconds(1) };
        Assert.NotNull(BuiltInRefresh.Evaluate(block, context, out _));
        Assert.NotNull(BuiltInRefresh.Evaluate(block, context with { IconSet = IconSet.Unicode }, out _));
        Assert.Equal(1, block.Collections);
        BuiltInRefresh.Evaluate(block, context with { BlockTimeoutMilliseconds = 3000 }, out _);
        Assert.Equal(2, block.Collections);
    }

    [Fact]
    public async Task SaturationDoesNotStartUnboundedReplacementWorkers() {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        RefreshValue<int>[] entries = [.. Enumerable.Range(0, 4).Select(_ => new RefreshValue<int>())];
        int started = 0;
        try {
            foreach (RefreshValue<int>? entry in entries) {
                entry.Get(_ => {
                    Interlocked.Increment(ref started);
                    return release.Task;
                },
                    TimeSpan.Zero, TimeSpan.FromMilliseconds(150), TimeSpan.Zero);
            }

            await WaitUntil(() => Volatile.Read(ref started) == 4);
            await WaitUntil(() => entries.All(entry => entry.Snapshot().Outcome == RefreshOutcome.TimedOut));
            Assert.Equal(0, RefreshWorkers.Slots.CurrentCount);
            using var fifth = new RefreshValue<int>();
            fifth.Get(_ => {
                Interlocked.Increment(ref started);
                return Task.FromResult(1);
            },
                TimeSpan.Zero, TimeSpan.FromMilliseconds(50), TimeSpan.Zero);
            await WaitUntil(() => fifth.Snapshot().Outcome == RefreshOutcome.TimedOut);
            Assert.Equal(4, started);
        }
        finally {
            release.TrySetResult(1);
            await WaitUntil(() => entries.All(entry => !entry.Snapshot().IsBusy));
            foreach (RefreshValue<int>? entry in entries) {
                entry.Dispose();
            }
        }

        Assert.Equal(4, RefreshWorkers.Slots.CurrentCount);
    }

    private sealed class CountingBlock : IRefreshingBlock {
        private readonly string scope = Guid.NewGuid().ToString();
        internal int Collections;
        public BlockMetadata Metadata { get; } = new(StatusLineBlockKind.Clock, StatusLineAlignment.Right, ("", "", ""));
        public TimeSpan Interval => TimeSpan.FromHours(1);
        public CollectionScope Scope(BlockContext context) => new(scope, string.Empty);
        public Task<CollectedBlock?> CollectAsync(BlockContext context, CancellationToken token) {
            Interlocked.Increment(ref Collections);
            return Task.FromResult<CollectedBlock?>(new CollectedBlock(icons => (icons.ToString(), "sample", StatusLineAlignment.Right, default(TerminalColor))));
        }
    }

    private static async Task WaitUntil(Func<bool> condition) {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) {
            await Task.Delay(10, timeout.Token);
        }
    }
}
