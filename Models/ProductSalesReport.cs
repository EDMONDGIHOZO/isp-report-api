namespace isp_report_api.Models;

/// <summary>Single row from product-level weekly aggregation (ISP, Product, Period, Purchases, Category).</summary>
public class ProductSalesRawRow
{
    public string Isp { get; set; } = string.Empty;
    public string FProdName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int Purchases { get; set; }
}

/// <summary>KPI block for the product sales dashboard.</summary>
public class ProductSalesKpis
{
    public long TotalPurchases { get; set; }
    public double AverageWeeklyPurchases { get; set; }
    public string TopProduct { get; set; } = string.Empty;
    public string TopIsp { get; set; } = string.Empty;
    public string BestWeek { get; set; } = string.Empty;
    public string WorstWeek { get; set; } = string.Empty;
    public double? WeekOverWeekGrowthPercent { get; set; }
}

/// <summary>One point for the weekly trend (e.g. line chart).</summary>
public class WeeklyTrendItem
{
    public string Period { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string GroupLabel { get; set; } = string.Empty;
    public int Purchases { get; set; }
}

/// <summary>ISP comparison point (e.g. stacked column: week, isp, purchases, contribution percent).</summary>
public class IspComparisonItem
{
    public string Period { get; set; } = string.Empty;
    public string Isp { get; set; } = string.Empty;
    public int Purchases { get; set; }
    public double ContributionPercent { get; set; }
}

/// <summary>Top product entry for horizontal bar.</summary>
public class TopProductItem
{
    public string ProductName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int Purchases { get; set; }
    public double ProductSharePercent { get; set; }
}

/// <summary>Single cell for heatmap: product, period, purchases.</summary>
public class HeatmapCell
{
    public string Product { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public int Purchases { get; set; }
}

/// <summary>Pivot row: ISP → Product → Week hierarchy. Weeks as key-value for flexibility.</summary>
public class PivotTableRow
{
    public string Isp { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int TotalPurchases { get; set; }
    public Dictionary<string, int> Weeks { get; set; } = new();
}

/// <summary>Full response for GET /api/sales/products.</summary>
public class ProductSalesResponse
{
    public ProductSalesKpis Kpis { get; set; } = new();
    public List<WeeklyTrendItem> WeeklyTrend { get; set; } = new();
    public List<IspComparisonItem> IspComparison { get; set; } = new();
    public List<TopProductItem> TopProducts { get; set; } = new();
    public List<HeatmapCell> HeatmapMatrix { get; set; } = new();
    public List<PivotTableRow> PivotTable { get; set; } = new();
    public List<ProductSalesRawRow> RawData { get; set; } = new();
}
