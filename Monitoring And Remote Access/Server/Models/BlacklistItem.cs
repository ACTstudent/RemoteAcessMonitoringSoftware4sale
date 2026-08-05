using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class BlacklistItem
    {
        [Key]
        public int BlacklistItemId { get; set; }

        [Required]
        [StringLength(20)]
        public string TargetType { get; set; } = string.Empty; // Website | Application

        [Required]
        [StringLength(300)]
        public string Value { get; set; } = string.Empty;

        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
