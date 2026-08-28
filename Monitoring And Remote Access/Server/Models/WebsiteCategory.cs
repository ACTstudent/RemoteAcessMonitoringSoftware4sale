using System.ComponentModel.DataAnnotations;

namespace Server.Models;

public class WebsiteCategory
{
    [Key]
    public int WebsiteCategoryId { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(300)]
    public string DomainPattern { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    [StringLength(20)]
    public string Mode { get; set; } = "Block";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
