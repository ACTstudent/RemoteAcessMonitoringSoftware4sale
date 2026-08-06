using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Class
    {
        [Key]
        public int ClassId { get; set; }

        [Required]
        [StringLength(100)]
        public string ClassName { get; set; } = string.Empty;

        [StringLength(50)]
        public string Section { get; set; } = string.Empty;

        [StringLength(100)]
        public string Subject { get; set; } = string.Empty;

        [StringLength(50)]
        public string GradeLevel { get; set; } = string.Empty;

        [StringLength(100)]
        public string Schedule { get; set; } = string.Empty;

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<ClassStudent> ClassStudents { get; set; } = new List<ClassStudent>();
    }
}