using Microsoft.EntityFrameworkCore;
using isp_report_api.Models;

namespace isp_report_api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<OtpCode> OtpCodes { get; set; }
    public DbSet<CacheEntry> CacheEntries { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<CacheEntry>()
            .HasIndex(c => c.CacheKey)
            .IsUnique();

        modelBuilder.Entity<CacheEntry>()
            .HasIndex(c => c.ExpiresAt);

        modelBuilder.Entity<CacheEntry>()
            .HasIndex(c => c.CacheType);
    }
}
