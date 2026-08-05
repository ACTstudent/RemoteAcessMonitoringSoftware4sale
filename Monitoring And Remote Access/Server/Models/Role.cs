using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Role
    {
        [Key]
        public int RoleId { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
    }
}
