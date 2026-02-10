using isp_report_api.Models;
using isp_report_api.Repository;

namespace isp_report_api.Services;

public interface IIspReportService
{
    Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllIspNamesAsync();
    Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter);
}

public class IspReportService : IIspReportService
{
    private readonly IIspReportRepository _repository;

    public IspReportService(IIspReportRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter)
    {
        return await _repository.GetMonthlyReportsAsync(filter);
    }

    public async Task<IEnumerable<string>> GetAllIspNamesAsync()
    {
        return await _repository.GetAllIspNamesAsync();
    }

    public async Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter)
    {
        return await _repository.GetPrepaidStatsAsync(filter);
    }
}
