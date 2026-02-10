namespace isp_report_api.Models;

public class PrepaidStats
{
    public int TotalPurchases { get; set; }
    public decimal AveragePurchases { get; set; }
    public decimal MonthOverMonthGrowth { get; set; }
    public IspStat? TopIspByAmount { get; set; }
    public IspStat? TopIspByPurchases { get; set; }
    public IspStat? LowestIsp { get; set; }
    public MonthStat? HighestMonth { get; set; }
    public MonthStat? LowestMonth { get; set; }
}

public class IspStat
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Purchases { get; set; }
}

public class MonthStat
{
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Purchases { get; set; }
}
