namespace isp_report_api.Models;

public class TrafficReport
{
    public string UDay { get; set; } = string.Empty;
    public string Isp { get; set; } = string.Empty;
    public int Subs { get; set; }
    public decimal UsgGb { get; set; }
}

public class TrafficReportSeries
{
    public string Isp { get; set; } = string.Empty;
    public List<TrafficReport> Points { get; set; } = new();
}

