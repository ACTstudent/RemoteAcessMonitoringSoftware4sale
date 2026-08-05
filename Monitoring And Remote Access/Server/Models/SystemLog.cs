using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class SystemLog
    {
        [Key]
        public int SystemLogId { get; set; }

        [Required]
        [StringLength(20)]
        public string Level { get; set; } = string.Empty; // Info | Warning | Error | Critical

        [Required]
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? StackTrace { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
