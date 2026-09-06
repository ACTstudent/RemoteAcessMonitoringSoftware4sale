using Microsoft.Extensions.Logging;

namespace Server.Services;

/// <summary>
/// Decides whether an unhandled request failure should be written to the
/// database, and builds the line that gets written.
///
/// Two problems this exists to solve.
///
/// The first is that the old handler recorded the literal sentence "An
/// unexpected server error occurred." and, outside Development, nothing else.
/// No type, no path, no identifier. A teacher reporting "it broke" and a row in
/// SystemLogs could not be connected, so the log was unusable for the only
/// purpose it had.
///
/// The second is volume. The handler writes to the database on the request
/// path. One failing dependency means every request fails, and every failure
/// opens a scope and writes a row - so the database gets hit hardest exactly
/// when it is least able to cope, and the evidence is thousands of identical
/// rows. Repeats of the same failure are counted and written at most once a
/// minute.
/// </summary>
public sealed class RequestFailureLog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _seen = new();
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _window;

    /// <summary>Distinct failure signatures tracked before the table is cleared.</summary>
    private const int MaxTrackedSignatures = 200;

    public RequestFailureLog(Func<DateTime>? clock = null, TimeSpan? window = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    private sealed class Entry
    {
        public DateTime LastWritten;
        public int SuppressedSinceLastWrite;
    }

    /// <summary>
    /// What identifies "the same failure again": the exception type and the
    /// route it happened on. Deliberately not the message, which often carries
    /// an id and would make every occurrence look distinct.
    /// </summary>
    public static string SignatureOf(Exception exception, string path) =>
        $"{exception.GetType().FullName}|{path}";

    /// <summary>
    /// True when this failure should be written. When it returns false the
    /// occurrence is counted and reported with the next write.
    /// </summary>
    public bool ShouldWrite(Exception exception, string path, out int suppressedSincePrevious)
    {
        var signature = SignatureOf(exception, path);
        var now = _clock();

        lock (_gate)
        {
            if (_seen.Count >= MaxTrackedSignatures && !_seen.ContainsKey(signature))
            {
                // A pathological variety of failures should not become a memory
                // leak. Starting again loses suppression counts, which matters
                // far less than bounded memory.
                _seen.Clear();
            }

            if (_seen.TryGetValue(signature, out var entry) && now - entry.LastWritten < _window)
            {
                entry.SuppressedSinceLastWrite++;
                suppressedSincePrevious = 0;
                return false;
            }

            suppressedSincePrevious = entry?.SuppressedSinceLastWrite ?? 0;
            _seen[signature] = new Entry { LastWritten = now, SuppressedSinceLastWrite = 0 };
            return true;
        }
    }

    /// <summary>
    /// The message stored against the failure.
    ///
    /// Carries the exception type, the request that failed and the correlation
    /// id the user is shown, which is the minimum needed to act on a report.
    /// It deliberately does not carry the query string, which routinely holds
    /// identifiers and filter values, nor the exception message, which for a
    /// database failure can quote the row it was writing. Those go in the stack
    /// trace field, and only in Development.
    /// </summary>
    public static string Describe(
        Exception exception, string method, string path, string correlationId, int suppressed)
    {
        var line = $"Unhandled {exception.GetType().Name} on {method} {path} " +
                   $"(correlation {correlationId})";
        return suppressed > 0
            ? $"{line}. {suppressed} identical failure(s) since the previous entry were not recorded."
            : line;
    }
}
