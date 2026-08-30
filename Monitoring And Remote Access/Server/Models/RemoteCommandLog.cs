using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class RemoteCommandLog
{
    [Key]
    public int RemoteCommandLogId { get; set; }
    public int? RemoteControlSessionId { get; set; }
    public int TeacherId { get; set; }

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Command { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Details { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
