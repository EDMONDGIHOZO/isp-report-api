namespace isp_report_api.Models;

public class PostpaidReport
{
    public string Period { get; set; } = string.Empty;
    public string Isp { get; set; } = string.Empty;
    public decimal EWallet { get; set; }
}

public class PostpaidReportSeries
{
    public string Isp { get; set; } = string.Empty;
    public List<PostpaidReport> Points { get; set; } = new();
}