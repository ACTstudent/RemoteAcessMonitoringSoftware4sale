using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class WebsiteUsageLog
{
    [Key]
    public int WebsiteUsageLogId { get; set; }
    public int? StudentId { get; set; }
    public Student? Student { get; set; }

    [Required, StringLength(300)]
    public string Domain { get; set; } = string.Empty;

    [StringLength(50)]
    public string Browser { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
