using isp_report_api.Models;

namespace isp_report_api.Repository;

/// <summary>
/// Data-access contract for the weekly sales dashboard.
/// </summary>
public interface ISalesRepository
{
    /// <summary>
    /// Returns aggregated weekly sales rows, optionally filtered by ISP name
    /// and date range (YYYY-MM format).
    /// </summary>
    Task<IEnumerable<WeeklySalesStat>> GetWeeklySalesAsync(
        string? ispName,
        string? from = null,
        string? to = null
    );
}
