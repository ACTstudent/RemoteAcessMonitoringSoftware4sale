using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class IdleInterval
{
    [Key]
    public int IdleIntervalId { get; set; }

    [Required, StringLength(100)]
    public string ConnectionId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string StudentId { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string PcName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
