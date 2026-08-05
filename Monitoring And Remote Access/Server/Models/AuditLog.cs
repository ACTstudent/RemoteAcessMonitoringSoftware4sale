using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class AuditLog
    {
        [Key]
        public int AuditLogId { get; set; }

        [StringLength(20)]
        public string UserType { get; set; } = string.Empty; // Admin | Teacher | Student | System

        public int? UserId { get; set; }

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty;

        [StringLength(1000)]
        public string Details { get; set; } = string.Empty;

        [StringLength(50)]
        public string? IpAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
