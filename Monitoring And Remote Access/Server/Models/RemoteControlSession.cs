using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class RemoteControlSession
{
    [Key]
    public int RemoteControlSessionId { get; set; }
    public int TeacherId { get; set; }

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ConnectionId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
