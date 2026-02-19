using System.ComponentModel.DataAnnotations;

namespace isp_report_api.Models;

public class Role
{
    [Key] public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<RolePage> RolePages { get; set; } = new List<RolePage>();
}
