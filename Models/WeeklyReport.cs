namespace isp_report_api.Models;

public class WeeklyReport
{
    public string Week { get; set; } = string.Empty; // Format: "YYYY-MM-WW" or "Week X"
    public int Purchase { get; set; }
    public decimal Amount { get; set; }
}

public class WeeklyReportSeries
{
    public string Isp { get; set; } = string.Empty;
    public List<WeeklyReport> Points { get; set; } = new();
}
