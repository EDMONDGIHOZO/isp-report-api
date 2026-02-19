namespace isp_report_api.Models;

/// <summary>
/// DTO representing aggregated weekly sales data per ISP.
/// Maps directly to the Oracle query result columns.
/// </summary>
public class WeeklySalesStat
{
    /// <summary>ISP name (SP_NAME).</summary>
    public string Isp { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable period label, e.g. "W1 (21/11 to 27/11)".
    /// </summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Wholesale amount in RWF (rounded).</summary>
    public decimal WholesaleRwf { get; set; }

    /// <summary>Retail amount in RWF.</summary>
    public decimal RetailRwf { get; set; }

    /// <summary>Total purchase count for this ISP × Week.</summary>
    public int Purchases { get; set; }

    /// <summary>Margin percentage: ((Retail - Wholesale) / Retail) * 100.</summary>
    public decimal MarginPercent { get; set; }
}
