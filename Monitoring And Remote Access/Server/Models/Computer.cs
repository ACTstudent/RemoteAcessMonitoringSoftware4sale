using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Computer
    {
        [Key]
        public int ComputerId { get; set; }

        [Required]
        [StringLength(50)]
        public string LaboratoryStation { get; set; } = string.Empty;

        [StringLength(50)]
        public string Status { get; set; } = "Available";

        public string? AssignedTo { get; set; }
    }
}
