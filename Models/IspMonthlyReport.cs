namespace isp_report_api.Models;

public class IspMonthlyReport
{
    public string UDay { get; set; } = string.Empty;
    public int Purchase { get; set; }
    public decimal Amount { get; set; }
}
