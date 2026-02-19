using System.ComponentModel.DataAnnotations;

namespace isp_report_api.Models;

public class RolePage
{
    [Key] public int Id { get; set; }

    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string PageKey { get; set; } = string.Empty;
}
