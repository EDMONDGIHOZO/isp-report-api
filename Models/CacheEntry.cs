using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace isp_report_api.Models;

[Table("cache_entries")]
public class CacheEntry
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("cache_key")]
    [StringLength(500)]
    public string CacheKey { get; set; } = string.Empty;

    [Required]
    [Column("cache_type")]
    [StringLength(100)]
    public string CacheType { get; set; } = string.Empty;

    [Required]
    [Column("cached_data", TypeName = "LONGTEXT")]
    public string CachedData { get; set; } = string.Empty;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("filter_hash")]
    [StringLength(64)]
    public string? FilterHash { get; set; }
}
