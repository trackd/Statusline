using System.Management.Automation.Runspaces;

namespace StatusLine;

// Runspace disposal does not guarantee IModuleAssemblyCleanup.OnRemove is called.
internal static class RunspaceLifecycle {
    internal static void Track(Runspace runspace) {
        ScriptBlockRuntime.TrackOwner(runspace);
        runspace.StateChanged -= OnStateChanged;
        runspace.StateChanged += OnStateChanged;
    }

    internal static void Untrack(Runspace runspace) {
        runspace.StateChanged -= OnStateChanged;
        ScriptBlockRuntime.UntrackOwner(runspace.InstanceId);
    }

    private static void OnStateChanged(object? sender, RunspaceStateEventArgs arguments) {
        if (sender is not Runspace runspace || arguments.RunspaceStateInfo.State is not (RunspaceState.Closed or RunspaceState.Broken)) {
            return;
        }

        Untrack(runspace);
        try {
            StatusLineUpdater.ReleaseOwner(runspace.InstanceId);
        }
        catch (TimeoutException exception) {
            // Keep ownership while output is blocked; a later start can retire the dead worker.
            StatusLineUpdater.ReportError(exception.Message);
        }
        finally {
            PromptSession.ReleaseOwner(runspace.InstanceId);
            ScriptBlockRuntime.Reset(runspace.InstanceId);
        }
    }
}
