using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Server.Models
{
    public class ClassStudent
    {
        [Key]
        public int ClassStudentId { get; set; }

        public int ClassId { get; set; }

        [JsonIgnore]
        public Class? Class { get; set; }

        public int StudentId { get; set; }

        [JsonIgnore]
        public Student? Student { get; set; }

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    }
}