using System.Diagnostics;

namespace StatusLine.Refreshing;

internal enum RefreshOutcome { Pending, Success, Fault, TimedOut, Cancelled }

internal readonly record struct RefreshSnapshot<T>(T? Value, bool HasValue, DateTime? LastUpdated,
    bool IsBusy, RefreshOutcome Outcome, string? Error);

// Limits even non-cooperative native/filesystem work. A timed-out operation retains its slot
// until it actually exits; starting replacements would accumulate abandoned workers.
internal static class RefreshWorkers {
    internal static readonly SemaphoreSlim Slots = new(4);
    private static long nextCollection;
    internal static long NextCollectionId() => Interlocked.Increment(ref nextCollection);
}

internal sealed class RefreshValue<T> : IDisposable {
    internal long CollectionId { get; } = RefreshWorkers.NextCollectionId();
    private readonly Lock gate = new();
    private T? value;
    private bool hasValue;
    private bool busy;
    private bool disposed;
    private long nextDue;
    private Task? notification;
    private CancellationTokenSource? cancellation;
    private string? lastError;
    private DateTime? lastUpdated;
    private RefreshOutcome outcome;

    internal RefreshSnapshot<T> Snapshot() {
        lock (gate) {
            return new(value, hasValue, lastUpdated, busy, outcome, lastError);
        }
    }

    public bool IsBusy {
        get {
            lock (gate) {
                return busy;
            }
        }
    }
    public T? Get(Func<CancellationToken, Task<T>> read, TimeSpan interval, TimeSpan timeout, TimeSpan wait) =>
        Read(read, static (reader, token) => reader(token), interval, timeout, wait).Value;

    public RefreshSnapshot<T> Read<TState>(TState state, Func<TState, CancellationToken, Task<T>> read,
        TimeSpan interval, TimeSpan timeout, TimeSpan wait) {
        Task? pending;
        lock (gate) {
            if (!disposed && !busy && Stopwatch.GetTimestamp() >= nextDue) {
                busy = true;
                Start(state, read, interval, timeout);
            }

            pending = notification;
        }

        if (wait > TimeSpan.Zero) {
            pending?.Wait(wait < timeout ? wait : timeout);
        }

        return Snapshot();
    }

    private void Start<TState>(TState state, Func<TState, CancellationToken, Task<T>> read,
        TimeSpan interval, TimeSpan timeout) {
        var source = new CancellationTokenSource(timeout);
        cancellation = source;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notification = completed.Task;
        // ExecuteAsync dispatches collection before synchronous provider code can run. The
        // supervisor itself needs no second worker hop.
        _ = RefreshAsync(token => read(state, token), interval, source, completed);
    }

    private async Task RefreshAsync(Func<CancellationToken, Task<T>> read, TimeSpan interval,
        CancellationTokenSource source, TaskCompletionSource completed) {
        Task<T> work = ExecuteAsync(read, source.Token);
        try {
            T result = await work.WaitAsync(source.Token).ConfigureAwait(false);
            lock (gate) {
                if (!disposed && !source.IsCancellationRequested) {
                    value = result;
                    hasValue = true;
                    lastError = null;
                    outcome = RefreshOutcome.Success;
                    lastUpdated = DateTime.Now;
                }
                else if (source.IsCancellationRequested) {
                    lastError = "Refresh timed out or was cancelled.";
                    outcome = disposed ? RefreshOutcome.Cancelled : RefreshOutcome.TimedOut;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) {
            lock (gate) {
                if (exception is OperationCanceledException) {
                    lastError = "Refresh timed out or was cancelled.";
                    outcome = disposed ? RefreshOutcome.Cancelled : RefreshOutcome.TimedOut;
                }
                else {
                    lastError = exception.Message;
                    outcome = RefreshOutcome.Fault;
                }
            }
        }
        finally {
            completed.TrySetResult();
            StatusLineUpdater.Wake();
        }

        // Observe late faults and discard late results before permitting another refresh.
        try {
            await work.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }

        lock (gate) {
            nextDue = Stopwatch.GetTimestamp() + (long)(interval.TotalSeconds * Stopwatch.Frequency);
            busy = false;
            cancellation = null;
        }

        source.Dispose();
    }

    private static async Task<T> ExecuteAsync(Func<CancellationToken, Task<T>> read, CancellationToken token) {
        await RefreshWorkers.Slots.WaitAsync(token).ConfigureAwait(false);
        try {
            // The extra dispatch is intentional: synchronous code before the first await must
            // not prevent the supervisor from observing its deadline.
            return await Task.Run(() => {
                token.ThrowIfCancellationRequested();
                return read(token);
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally {
            RefreshWorkers.Slots.Release();
        }
    }

    public void Dispose() {
        lock (gate) {
            disposed = true;
            cancellation?.Cancel();
        }
    }
}

internal sealed class RefreshCache<TKey, TValue>(int capacity, IEqualityComparer<TKey>? comparer = null) where TKey : notnull {
    private readonly Lock gate = new();
    private readonly Dictionary<TKey, RefreshValue<TValue>> entries = new(comparer);
    private readonly Queue<TKey> order = new();

    public (TKey Key, RefreshValue<TValue> Value)[] Inspect() {
        lock (gate) {
            return [.. entries.Select(pair => (pair.Key, pair.Value))];
        }
    }

    public RefreshValue<TValue>? Get(TKey key) {
        lock (gate) {
            if (entries.TryGetValue(key, out RefreshValue<TValue>? existing)) {
                return existing;
            }

            int candidates = order.Count;
            while (entries.Count >= capacity && candidates-- > 0) {
                TKey oldest = order.Dequeue();
                if (entries[oldest].IsBusy) {
                    order.Enqueue(oldest);
                    continue;
                }

                entries.Remove(oldest, out RefreshValue<TValue>? removed);
                removed!.Dispose();
            }

            if (entries.Count >= capacity) {
                return null;
            }

            var entry = new RefreshValue<TValue>();
            entries.Add(key, entry);
            order.Enqueue(key);
            return entry;
        }
    }
}
