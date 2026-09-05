namespace Server.Extensions;

/// <summary>
/// Timestamps are persisted in UTC. SQLite returns them with
/// <see cref="DateTimeKind.Unspecified"/>, so converting straight to local time
/// would leave them unchanged and show UTC to the user. These helpers pin the
/// stored value to UTC first, then convert, so views render local time wherever
/// the server happens to be.
/// </summary>
public static class DateTimeDisplayExtensions
{
    public static DateTime ToDisplayLocal(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value,
        DateTimeKind.Utc => value.ToLocalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime()
    };

    public static DateTime? ToDisplayLocal(this DateTime? value) =>
        value.HasValue ? value.Value.ToDisplayLocal() : null;
}
