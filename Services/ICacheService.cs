using isp_report_api.Models;

namespace isp_report_api.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string cacheKey) where T : class;
    Task SetAsync<T>(string cacheKey, T value, TimeSpan? expiration = null) where T : class;
    Task RemoveAsync(string cacheKey);
    Task RemoveByTypeAsync(string cacheType);
    Task ClearExpiredAsync();
    string GenerateCacheKey(string cacheType, IspReportFilter filter);
    string GenerateFilterHash(IspReportFilter filter);
}
