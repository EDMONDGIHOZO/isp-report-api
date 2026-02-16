namespace isp_report_api.Models;

public class PostpaidStats
{
    public decimal TotalEWallet { get; set; }
    public decimal AverageEWallet { get; set; }
    public decimal MonthOverMonthGrowth { get; set; }
    public IspStat? TopIspByEWallet { get; set; }
    public IspStat? LowestIsp { get; set; }
    public MonthStat? HighestMonth { get; set; }
    public MonthStat? LowestMonth { get; set; }
}
