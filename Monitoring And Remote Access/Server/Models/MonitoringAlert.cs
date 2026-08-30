using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models;

public enum MonitoringAlertStatus
{
    Open,
    Acknowledged,
    Dismissed
}

public class MonitoringAlert
{
    private string _groupKey = string.Empty;
    private DateTime _firstSeenAt;
    private DateTime _lastSeenAt;

    [Key]
    public int MonitoringAlertId { get; set; }

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string Severity { get; set; } = "Warning";

    [Required, StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string Message { get; set; } = string.Empty;

    public bool IsAcknowledged { get; set; }

    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedByTeacherId { get; set; }

    public DateTime? DismissedAt { get; set; }
    public int? DismissedByTeacherId { get; set; }

    [StringLength(500)]
    public string? DismissalReason { get; set; }

    [StringLength(100)]
    public string DedupeKey { get; set; } = string.Empty;

    [Required, StringLength(350)]
    public string GroupKey
    {
        get => string.IsNullOrWhiteSpace(_groupKey)
            ? CreateGroupKey(StudentId, PcName, DedupeKey, Title)
            : _groupKey;
        set => _groupKey = value;
    }

    public int OccurrenceCount { get; set; } = 1;

    public DateTime FirstSeenAt
    {
        get => _firstSeenAt == default ? CreatedAt : _firstSeenAt;
        set => _firstSeenAt = value;
    }

    public DateTime LastSeenAt
    {
        get => _lastSeenAt == default ? CreatedAt : _lastSeenAt;
        set => _lastSeenAt = value;
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The three states are mutually exclusive. Reopening clears all lifecycle metadata.
    [NotMapped]
    public MonitoringAlertStatus Status => DismissedAt.HasValue
        ? MonitoringAlertStatus.Dismissed
        : IsAcknowledged
            ? MonitoringAlertStatus.Acknowledged
            : MonitoringAlertStatus.Open;

    public static string CreateGroupKey(string? studentId, string? pcName, string? dedupeKey, string? title)
    {
        static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

        var identity = string.IsNullOrWhiteSpace(dedupeKey) ? title : dedupeKey;
        return $"{Normalize(studentId)}|{Normalize(pcName)}|{Normalize(identity)}";
    }
}
