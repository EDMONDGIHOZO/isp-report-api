namespace isp_report_api.Models;

public class IspMonthlyReportSeries
{
    public string Isp { get; set; } = string.Empty;
    public List<IspMonthlyReport> Points { get; set; } = new();
}

