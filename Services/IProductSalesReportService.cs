using isp_report_api.Models;

namespace isp_report_api.Services;

public interface IProductSalesReportService
{
    Task<ProductSalesResponse> GetProductSalesReportAsync(
        DateTime? startDate,
        DateTime? endDate,
        IReadOnlyList<string>? ispFilter = null,
        IReadOnlyList<string>? productFilter = null,
        IReadOnlyList<string>? categoryFilter = null,
        IReadOnlyList<string>? excludeIsps = null,
        int topN = 10,
        string? groupBy = null
    );
}
