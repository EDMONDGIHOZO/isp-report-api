namespace isp_report_api.Models;

public class ProductWeeklyBreakdown
{
    public string ProductName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public int Purchases { get; set; }
}

