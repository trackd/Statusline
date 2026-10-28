using System.Diagnostics;

namespace StatusLine.Refreshing;

/// <summary>Monotonic activity tracking shared by idle notifications and the refresh loop.</summary>
internal sealed class RefreshActivity {
    private readonly TimeSpan timeout;
    public RefreshActivity(TimeSpan timeout) {
        this.timeout = timeout;
    }
    private long lastActivity = Stopwatch.GetTimestamp();
    private int idle;
    public bool IsIdle => Volatile.Read(ref idle) != 0;
    public TimeSpan InactiveFor => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity));
    public void Touch() {
        Interlocked.Exchange(ref lastActivity, Stopwatch.GetTimestamp());
        Volatile.Write(ref idle, 0);
    }
    public bool CheckIdle() {
        if (timeout > TimeSpan.Zero && Stopwatch.GetElapsedTime(Interlocked.Read(ref lastActivity)) >= timeout) {
            Volatile.Write(ref idle, 1);
        }

        return IsIdle;
    }
}
