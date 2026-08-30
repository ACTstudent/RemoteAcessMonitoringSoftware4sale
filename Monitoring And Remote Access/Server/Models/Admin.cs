using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Admin
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEndUtc { get; set; }
    }
}
