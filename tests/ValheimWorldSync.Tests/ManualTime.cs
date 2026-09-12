namespace ValheimWorldSync.Tests;

internal sealed class ManualTime : TimeProvider
{
    private long ticks;
    private readonly List<ManualTimer> timers = [];
    private readonly object gate = new();
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (gate) return ticks; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period); timers.Add(timer);
            return timer;
        }
    }
    public void Advance(TimeSpan amount)
    {
        List<Action> callbacks = [];
        lock (gate)
        {
            ticks += amount.Ticks;
            foreach (var timer in timers.ToArray())
            {
                if (timer.Due > ticks || timer.Due == long.MaxValue) continue;
                timer.Due = timer.Period > 0 ? ticks + timer.Period : long.MaxValue;
                callbacks.Add(timer.Fire);
            }
        }
        foreach (var callback in callbacks) callback();
    }
    private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public long Due { get; set; }
        public long Period { get; private set; }
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                if (disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }
        public void Fire() { if (!disposed) callback(state); }
        public void Dispose() { lock (owner.gate) { disposed = true; owner.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
