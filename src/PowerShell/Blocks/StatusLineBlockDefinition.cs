using System.Drawing;
using System.Management.Automation;
using StatusLine.Refreshing;

namespace StatusLine;

/// <summary>A user-defined block whose script block is re-run in the background on its own interval.</summary>
public sealed class StatusLineBlockDefinition : IDisposable {
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, RefreshValue<List<object>>> outputs = [];
    private bool disposed;
    private long appearanceVersion;
    internal long AppearanceVersion => Volatile.Read(ref appearanceVersion);

    private void SetAppearance<T>(ref T property, T value) {
        lock (gate) {
            property = value;
            Interlocked.Increment(ref appearanceVersion);
        }
    }

    internal (long Version, DefinitionAppearance Appearance) CaptureAppearance() {
        lock (gate) {
            return (appearanceVersion, new(Foreground, Background, Alignment, Padding, Icon, Bold, Italic, Underline));
        }
    }
    internal StatusLineBlockDefinition(string name, string scriptText, TimeSpan interval, StatusLineBlockKind? kind = null,
        int timeoutMilliseconds = 2000) {
        Name = name;
        ScriptText = scriptText;
        Interval = interval;
        Kind = kind;
        TimeoutMilliseconds = timeoutMilliseconds;
    }

    public string Name { get; }
    public StatusLineBlockKind? Kind { get; }
    public string? Padding { get; set => SetAppearance(ref field, value); }
    internal StatusLineAlignment EffectiveAlignment => Alignment ??
        (Kind is { } kind ? BlockCatalog.Get(kind).Metadata.Alignment : StatusLineAlignment.Right);

    public TimeSpan Interval { get; }

    public string? Icon { get; set => SetAppearance(ref field, value); }

    public TerminalColor? Foreground { get; set => SetAppearance(ref field, value); }

    public TerminalColor? Background { get; set => SetAppearance(ref field, value); }

    public StatusLineAlignment? Alignment {
        get;
        set {
            if (value is { } alignment && !Enum.IsDefined(alignment)) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetAppearance(ref field, value);
        }
    }

    public bool? Bold { get; set => SetAppearance(ref field, value); }

    public bool? Italic { get; set => SetAppearance(ref field, value); }

    public bool? Underline { get; set => SetAppearance(ref field, value); }

    public int TimeoutMilliseconds { get; }

    internal string ScriptText { get; }

    internal SegmentRefreshStatus GetDiagnostics(Guid owner) {
        lock (gate) {
            outputs.TryGetValue(owner, out RefreshValue<List<object>>? output);
            RefreshSnapshot<List<object>> snapshot = output?.Snapshot() ?? default;
            return new(Name, string.Empty, snapshot.LastUpdated, snapshot.Error, snapshot.Error is not null,
                snapshot.IsBusy, output?.CollectionId ?? 0, TimeoutMilliseconds, owner);
        }
    }

    internal RefreshValue<List<object>> GetOrCreateOutput(Guid owner, out bool created) {
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            created = false;
            if (!outputs.TryGetValue(owner, out RefreshValue<List<object>>? output)) {
                output = new();
                outputs.Add(owner, output);
                created = true;
            }

            return output;
        }
    }

    internal (List<object> Values, RefreshSnapshot<List<object>> Status) GetOutput(StatusLineState state, TimeSpan wait, Guid owner) {
        RefreshValue<List<object>> output = owner == Guid.Empty
            ? GetOrCreateOutput(owner, out _) : ScriptBlockRuntime.Acquire(this, owner);
        RefreshSnapshot<List<object>> snapshot = output.Read((Definition: this, State: state),
            static (request, token) => request.Definition.ReadAsync(request.State, token), Interval,
            TimeSpan.FromMilliseconds(TimeoutMilliseconds), wait);
        if (snapshot.HasValue) {
            return (snapshot.Value!, snapshot);
        }

        if (snapshot.Outcome == RefreshOutcome.Fault) {
            return ([new StatusLineBlock("Warning: " + snapshot.Error, Color.IndianRed, Color.Empty, EffectiveAlignment)], snapshot);
        }

        return ([], snapshot);
    }

    private async Task<List<object>> ReadAsync(StatusLineState state, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        using var powerShell = PowerShell.Create();
        powerShell.RunspacePool = ScriptBlockRuntime.Pool;
        // PowerShell can assign public init properties. Give the script its own snapshot so
        // it cannot change facts observed by other blocks in the same frame.
        powerShell.AddScript(ScriptText).AddArgument(state with { });
        Task<PSDataCollection<PSObject>> invocation = powerShell.InvokeAsync();
        using CancellationTokenRegistration stop = token.Register(() => {
            try {
                _ = powerShell.BeginStop(result => {
                    try {
                        powerShell.EndStop(result);
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or RuntimeException) { }
                }, null);
            }
            catch (Exception exception) when (exception is InvalidOperationException or RuntimeException) { }
        });
        using PSDataCollection<PSObject> result = await invocation.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (powerShell.Streams.Error.Count > 0) {
            throw new InvalidOperationException(powerShell.Streams.Error[0].ToString());
        }

        var values = new List<object>(result.Count);
        foreach (PSObject item in result) {
            if (item?.BaseObject is StatusLineBlock block) {
                values.Add(block);
            }
            else if (item?.BaseObject is not null) {
                values.Add(item.BaseObject);
            }
        }

        return values;
    }

    internal void ReleaseOwner(Guid owner) {
        lock (gate) {
            if (outputs.Remove(owner, out RefreshValue<List<object>>? output)) {
                output.Dispose();
            }
        }
    }

    public void Dispose() {
        lock (gate) {
            disposed = true;
            foreach (RefreshValue<List<object>> output in outputs.Values) {
                output.Dispose();
            }

            outputs.Clear();
        }
    }
}
