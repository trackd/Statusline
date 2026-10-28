using System.Management.Automation.Runspaces;
using System.Reflection;
using StatusLine.Refreshing;
using StatusLine.Utility;
using Xunit;

namespace StatusLine.Tests;

public sealed class RuntimeOwnershipTests {
    [Fact]
    public void ClosedOwnerCannotRegisterALateScriptCache() {
        using Runspace runspace = RunspaceFactory.CreateRunspace(InitialSessionState.Create());
        runspace.Open();
        using var block = new StatusLineBlockDefinition("owned", "'value'", TimeSpan.FromSeconds(1));
        ScriptBlockRuntime.TrackOwner(runspace);
        try {
            RefreshValue<List<object>> cache = ScriptBlockRuntime.Acquire(block, runspace.InstanceId);
            Assert.Equal(cache.CollectionId, block.GetDiagnostics(runspace.InstanceId).CollectionId);
            Assert.Contains(block, ScriptBlockRuntime.ForRunspace(runspace.InstanceId));

            ScriptBlockRuntime.UntrackOwner(runspace.InstanceId);
            ScriptBlockRuntime.Reset(runspace.InstanceId);

            Assert.Empty(ScriptBlockRuntime.ForRunspace(runspace.InstanceId));
            Assert.Equal(0, block.GetDiagnostics(runspace.InstanceId).CollectionId);
            Assert.Throws<InvalidOperationException>(() => ScriptBlockRuntime.Acquire(block, runspace.InstanceId));
            Assert.Equal(0, block.GetDiagnostics(runspace.InstanceId).CollectionId);
        }
        finally {
            ScriptBlockRuntime.UntrackOwner(runspace.InstanceId);
            ScriptBlockRuntime.Reset(runspace.InstanceId);
        }
    }

    [Fact]
    public void ExitWaitsForOwnerReplacementAndDoesNotCancelReplacement() {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        Type type = typeof(StatusLineUpdater);
        var gate = (Lock)type.GetField("Gate", flags)!.GetValue(null)!;
        FieldInfo ownerField = type.GetField("owner", flags)!;
        FieldInfo sourceField = type.GetField("cancellation", flags)!;
        object? originalOwner = ownerField.GetValue(null);
        object? originalSource = sourceField.GetValue(null);
        Guid previous = Guid.NewGuid(), replacement = Guid.NewGuid();
        using var retired = new CancellationTokenSource();
        using var current = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        Exception? failure = null;
        var callback = new Thread(() => {
            entered.Set();
            try {
                StatusLineUpdater.OnExit(previous);
            }
            catch (Exception exception) {
                failure = exception;
            }
        }) { IsBackground = true };
        try {
            lock (gate) {
                ownerField.SetValue(null, previous);
                sourceField.SetValue(null, retired);
                callback.Start();
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(SpinWait.SpinUntil(() => !callback.IsAlive ||
                    (callback.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
                Assert.True(callback.IsAlive);
                retired.Dispose();
                ownerField.SetValue(null, replacement);
                sourceField.SetValue(null, current);
            }

            Assert.True(callback.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            Assert.False(current.IsCancellationRequested);
        }
        finally {
            lock (gate) {
                ownerField.SetValue(null, originalOwner);
                sourceField.SetValue(null, originalSource);
            }

            callback.Join(TimeSpan.FromSeconds(5));
        }
    }
}
