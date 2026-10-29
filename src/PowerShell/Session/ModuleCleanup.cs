using System.Management.Automation;

namespace StatusLine;

/// <summary>Stops the updater and disposes script definition runspaces when the module is removed.</summary>
public sealed class ModuleCleanup : IModuleAssemblyCleanup, IModuleAssemblyInitializer {
    public void OnImport() {
        if (System.Management.Automation.Runspaces.Runspace.DefaultRunspace is { } runspace) {
            RunspaceLifecycle.Track(runspace);
        }

        PathInfo location = new SessionState().Path.CurrentLocation;
        if (location.Provider.Name == "FileSystem") {
            DirectoryDisplay.Initialize(location.ProviderPath);
        }
    }

    public void OnRemove(PSModuleInfo psModuleInfo) {
        bool stopped = false;
        try {
            StatusLineUpdater.Stop();
            stopped = !StatusLineUpdater.IsRunning;
        }
        finally {
            try {
                if (System.Management.Automation.Runspaces.Runspace.DefaultRunspace is { } runspace) {
                    try {
                        PromptSession.Disable(runspace);
                    }
                    finally {
                        RunspaceLifecycle.Untrack(runspace);
                    }
                }
            }
            finally {
                ScriptBlockRuntime.Reset();
                if (stopped) {
                    GpuSampler.Shared.Reset();
                }
            }
        }
    }
}
