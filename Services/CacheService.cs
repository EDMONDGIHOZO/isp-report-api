using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using isp_report_api.Data;
using isp_report_api.Models;
using Microsoft.EntityFrameworkCore;

namespace isp_report_api.Services;

public class CacheService : ICacheService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CacheService> _logger;
    private readonly TimeSpan _defaultExpiration;
    private readonly bool _cacheEnabled;

    public CacheService(
        AppDbContext dbContext,
        IConfiguration configuration,
        ILogger<CacheService> logger
    )
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        _cacheEnabled = _configuration.GetValue<bool>("Cache:EnableCache", true);
        _defaultExpiration = TimeSpan.FromHours(
            _configuration.GetValue<int>("Cache:DefaultExpirationHours", 24)
        );
    }

    public async Task<T?> GetAsync<T>(string cacheKey) where T : class
    {
        if (!_cacheEnabled)
        {
            return null;
        }

        try
        {
            var cacheEntry = await _dbContext
                .Set<CacheEntry>()
                .FirstOrDefaultAsync(c => c.CacheKey == cacheKey && c.ExpiresAt > DateTime.UtcNow);

            if (cacheEntry == null)
            {
                return null;
            }

            var data = JsonSerializer.Deserialize<T>(cacheEntry.CachedData);
            _logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache for key: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task SetAsync<T>(string cacheKey, T value, TimeSpan? expiration = null)
        where T : class
    {
        if (!_cacheEnabled)
        {
            return;
        }

        try
        {
            var expirationTime = DateTime.UtcNow.Add(expiration ?? _defaultExpiration);
            var serializedData = JsonSerializer.Serialize(value);

            var existingEntry = await _dbContext
                .Set<CacheEntry>()
                .FirstOrDefaultAsync(c => c.CacheKey == cacheKey);

            if (existingEntry != null)
            {
                existingEntry.CachedData = serializedData;
                existingEntry.ExpiresAt = expirationTime;
                existingEntry.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                var cacheType = ExtractCacheType(cacheKey);
                var filterHash = GenerateFilterHashFromKey(cacheKey);
                var newEntry = new CacheEntry
                {
                    CacheKey = cacheKey,
                    CacheType = cacheType,
                    CachedData = serializedData,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expirationTime,
                    FilterHash = filterHash,
                };
                _dbContext.Set<CacheEntry>().Add(newEntry);
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogDebug("Cache set for key: {CacheKey}, expires at: {ExpiresAt}", cacheKey, expirationTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache for key: {CacheKey}", cacheKey);
        }
    }

    public async Task RemoveAsync(string cacheKey)
    {
        try
        {
            var entry = await _dbContext
                .Set<CacheEntry>()
                .FirstOrDefaultAsync(c => c.CacheKey == cacheKey);

            if (entry != null)
            {
                _dbContext.Set<CacheEntry>().Remove(entry);
                await _dbContext.SaveChangesAsync();
                _logger.LogDebug("Cache removed for key: {CacheKey}", cacheKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache for key: {CacheKey}", cacheKey);
        }
    }

    public async Task RemoveByTypeAsync(string cacheType)
    {
        try
        {
            var entries = await _dbContext
                .Set<CacheEntry>()
                .Where(c => c.CacheType == cacheType)
                .ToListAsync();

            if (entries.Any())
            {
                _dbContext.Set<CacheEntry>().RemoveRange(entries);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Removed {Count} cache entries of type: {CacheType}", entries.Count, cacheType);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by type: {CacheType}", cacheType);
        }
    }

    public async Task ClearExpiredAsync()
    {
        try
        {
            var expiredEntries = await _dbContext
                .Set<CacheEntry>()
                .Where(c => c.ExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            if (expiredEntries.Any())
            {
                _dbContext.Set<CacheEntry>().RemoveRange(expiredEntries);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleared {Count} expired cache entries", expiredEntries.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing expired cache entries");
        }
    }

    public string GenerateCacheKey(string cacheType, IspReportFilter filter)
    {
        var filterHash = GenerateFilterHash(filter);
        return $"{cacheType}:{filterHash}";
    }

    public string GenerateFilterHash(IspReportFilter filter)
    {
        var filterString = $"{filter.FromPeriod ?? "null"}|{filter.ToPeriod ?? "null"}|{filter.IspName ?? "null"}|{filter.IncludeCurrentMonthWeekly}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(filterString));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string ExtractCacheType(string cacheKey)
    {
        var parts = cacheKey.Split(':');
        return parts.Length > 0 ? parts[0] : "unknown";
    }

    private string? GenerateFilterHashFromKey(string cacheKey)
    {
        var parts = cacheKey.Split(':');
        return parts.Length > 1 ? parts[1] : null;
    }
}
