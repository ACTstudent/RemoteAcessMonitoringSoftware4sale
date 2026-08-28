using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class MonitoringAlert
{
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
