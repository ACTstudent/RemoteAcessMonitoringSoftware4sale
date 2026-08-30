using System.ComponentModel.DataAnnotations;
using Shared.Contracts;

namespace Server.Models;

public sealed class BrowserMonitoringRecord
{
    [Key]
    public int BrowserMonitoringRecordId { get; set; }

    [Required, StringLength(100)]
    public string ConnectionId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Browser { get; set; } = string.Empty;

    public BrowserMonitoringMode Mode { get; set; }

    [StringLength(300)]
    public string? Detail { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
