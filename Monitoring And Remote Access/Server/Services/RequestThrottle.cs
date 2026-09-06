using System.Collections.Concurrent;

namespace Server.Services;

/// <summary>
/// A fixed-window request ceiling, keyed by caller address and path.
///
/// This lived inline in the request pipeline, where it could only be checked by
/// running a server and counting responses. It is a small amount of state with
/// several edges worth pinning - window rollover, the sweep, the hard key
/// ceiling - so it sits here where tests can reach it.
///
/// The clock is injected rather than read from <see cref="DateTime.UtcNow"/> so
/// a test can cross a window boundary without waiting a minute.
/// </summary>
public sealed class RequestThrottle
{
    /// <summary>Each unseen address adds a key, so entries must be swept or a caller rotating source addresses could grow this without bound.</summary>
    private readonly ConcurrentDictionary<string, (DateTime Started, int Count)> _windows = new();
    private readonly Func<DateTime> _clock;
    private readonly int _maxTrackedKeys;
    private long _lastSweepTicks;

    public static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SweepOlderThan = TimeSpan.FromMinutes(2);
    public const int DefaultMaxTrackedKeys = 20_000;

    public RequestThrottle(Func<DateTime>? clock = null, int maxTrackedKeys = DefaultMaxTrackedKeys)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _maxTrackedKeys = maxTrackedKeys;
        _lastSweepTicks = _clock().Ticks;
    }

    /// <summary>Number of keys currently tracked. Exposed so a test can prove the sweep runs.</summary>
    public int TrackedKeys => _windows.Count;

    /// <summary>
    /// Records one request against <paramref name="key"/> and reports whether it
    /// should be refused. A request that takes the count past
    /// <paramref name="limit"/> within the window is refused.
    /// </summary>
    public bool ShouldRefuse(string key, int limit)
    {
        var now = _clock();
        SweepIfDue(now);

        // If the map is already full, refusing an unseen key throttles the
        // request rather than letting the map expand further.
        if (_windows.Count >= _maxTrackedKeys && !_windows.ContainsKey(key))
        {
            return true;
        }

        var window = _windows.AddOrUpdate(
            key,
            (now, 1),
            (_, current) => now - current.Started >= WindowLength
                ? (now, 1)
                : (current.Started, current.Count + 1));

        return window.Count > limit;
    }

    /// <summary>Drops expired windows. One caller wins the exchange and sweeps; the rest carry on unblocked.</summary>
    private void SweepIfDue(DateTime now)
    {
        var previous = Interlocked.Read(ref _lastSweepTicks);
        if (now.Ticks - previous <= SweepInterval.Ticks ||
            Interlocked.CompareExchange(ref _lastSweepTicks, now.Ticks, previous) != previous)
        {
            return;
        }

        foreach (var tracked in _windows)
        {
            if (now - tracked.Value.Started > SweepOlderThan)
            {
                _windows.TryRemove(tracked.Key, out _);
            }
        }
    }
}
