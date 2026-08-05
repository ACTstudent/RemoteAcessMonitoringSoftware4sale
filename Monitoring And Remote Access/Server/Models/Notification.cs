using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        public int? StudentId { get; set; }

        [Required]
        [StringLength(20)]
        public string Type { get; set; } = string.Empty; // Warning | Alert | Info

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(1000)]
        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
