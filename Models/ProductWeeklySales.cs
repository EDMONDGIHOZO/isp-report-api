namespace isp_report_api.Models;

public class ProductWeeklySales
{
    public string Isp { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public decimal WholesaleRwf { get; set; }
    public decimal RetailRwf { get; set; }
    public int Purchases { get; set; }
    public decimal MarginPercent { get; set; }
}

