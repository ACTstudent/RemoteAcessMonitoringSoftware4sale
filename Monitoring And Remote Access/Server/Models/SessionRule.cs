using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class SessionRule
    {
        [Key]
        public int SessionRuleId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public int MaxDurationMinutes { get; set; } = 60;

        public bool AllowPause { get; set; } = true;

        public bool AllowRemoteControl { get; set; } = true;

        public bool IsDefault { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
