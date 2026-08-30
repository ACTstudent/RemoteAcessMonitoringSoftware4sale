using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class RestrictionRule
    {
        [Key]
        public int RestrictionRuleId { get; set; }

        [Required]
        [StringLength(20)]
        public string RuleType { get; set; } = string.Empty; // Application | Website

        [Required]
        [StringLength(200)]
        public string Target { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [StringLength(20)]
        public string Mode { get; set; } = "Block"; // Block | Allow (whitelist)

        public bool IsGlobal { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
