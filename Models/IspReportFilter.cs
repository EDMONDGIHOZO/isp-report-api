namespace isp_report_api.Models;

public class IspReportFilter
{
    public string? FromPeriod { get; set; }
    public string? ToPeriod { get; set; }
    public string? IspName { get; set; }
    public bool IncludeCurrentMonthWeekly { get; set; } = false;
}
