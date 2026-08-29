using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class ComputerStatusHistory
{
    [Key]
    public int ComputerStatusHistoryId { get; set; }

    public int ComputerId { get; set; }
    public Computer? Computer { get; set; }

    [Required, StringLength(50)]
    public string Status { get; set; } = string.Empty;

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [StringLength(20)]
    public string ChangedByType { get; set; } = string.Empty;

    public int? ChangedById { get; set; }
}
