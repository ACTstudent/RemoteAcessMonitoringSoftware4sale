using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class ActivityEvent
{
    [Key]
    public int ActivityEventId { get; set; }

    [Required, StringLength(100)]
    public string ConnectionId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string EventType { get; set; } = string.Empty;

    [StringLength(300)]
    public string? ApplicationName { get; set; }

    [StringLength(1000)]
    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}
