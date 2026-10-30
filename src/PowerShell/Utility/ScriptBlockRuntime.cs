using System.Management.Automation.Runspaces;
using StatusLine.Refreshing;
namespace StatusLine.Utility;

/// <summary>Lazy shared execution pool; weak references support diagnostics and module cleanup, not name lookup.</summary>
internal static class ScriptBlockRuntime {
    private static readonly Lock Gate = new();
    private static readonly Lock OwnerGate = new();
    private static readonly Dictionary<Guid, List<WeakReference<StatusLineBlockDefinition>>> Owners = [];
    private static readonly Dictionary<Guid, WeakReference<Runspace>> Runspaces = [];
    private static RunspacePool? pool;

    internal static void TrackOwner(Runspace runspace) {
        lock (OwnerGate) {
            Runspaces[runspace.InstanceId] = new(runspace);
        }
    }

    internal static void UntrackOwner(Guid owner) {
        lock (OwnerGate) {
            Runspaces.Remove(owner);
        }
    }

    internal static RefreshValue<List<object>> Acquire(StatusLineBlockDefinition block, Guid owner) {
        lock (Gate) {
            // Pool initialization imports this module on another thread while holding Gate.
            lock (OwnerGate) {
                if (!Runspaces.TryGetValue(owner, out WeakReference<Runspace>? reference) || !reference.TryGetTarget(out Runspace? runspace) ||
                    runspace.RunspaceStateInfo.State != RunspaceState.Opened) {
                    throw new InvalidOperationException("The script block's owning runspace is no longer active.");
                }
            }

            RefreshValue<List<object>> output = block.GetOrCreateOutput(owner, out bool created);
            if (!created) {
                return output;
            }

            if (!Owners.TryGetValue(owner, out List<WeakReference<StatusLineBlockDefinition>>? blocks)) {
                Owners[owner] = blocks = [];
            }

            blocks.RemoveAll(entry => !entry.TryGetTarget(out _));
            if (!blocks.Any(entry => entry.TryGetTarget(out StatusLineBlockDefinition? target) && ReferenceEquals(target, block))) {
                blocks.Add(new(block));
            }

            return output;
        }
    }

    internal static StatusLineBlockDefinition[] ForRunspace(Guid owner) {
        lock (Gate) {
            if (!Owners.TryGetValue(owner, out List<WeakReference<StatusLineBlockDefinition>>? blocks)) {
                return [];
            }

            return [.. blocks.Select(reference => reference.TryGetTarget(out StatusLineBlockDefinition? block) ? block : null).OfType<StatusLineBlockDefinition>()];
        }
    }

    internal static RunspacePool Pool {
        get {
            lock (Gate) {
                if (pool is null) {
                    var state = InitialSessionState.CreateDefault2();
                    state.ImportPSModule(Path.Combine(Path.GetDirectoryName(typeof(ScriptBlockRuntime).Assembly.Location)!, "StatusLine.psd1"));
                    pool = RunspaceFactory.CreateRunspacePool(state);
                    pool.SetMaxRunspaces(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
                    pool.Open();
                }

                return pool;
            }
        }
    }

    internal static void Reset() => Reset(Runspace.DefaultRunspace?.InstanceId ?? Guid.Empty);

    internal static void Reset(Guid owner) {
        RunspacePool? retired = null;
        lock (Gate) {
            if (Owners.Remove(owner, out List<WeakReference<StatusLineBlockDefinition>>? blocks)) {
                foreach (WeakReference<StatusLineBlockDefinition> reference in blocks) {
                    if (reference.TryGetTarget(out StatusLineBlockDefinition? block)) {
                        block.ReleaseOwner(owner);
                    }
                }

                if (Owners.Count == 0) {
                    retired = pool;
                    pool = null;
                }
            }
        }

        if (retired is not null) {
            _ = Task.Run(retired.Dispose);
        }
    }
}
