using isp_report_api.Models;
using isp_report_api.Repository;

namespace isp_report_api.Services;

public class ProductSalesReportService : IProductSalesReportService
{
    private readonly IProductSalesRepository _productSalesRepository;

    public ProductSalesReportService(IProductSalesRepository productSalesRepository)
    {
        _productSalesRepository = productSalesRepository;
    }

    public async Task<ProductSalesResponse> GetProductSalesReportAsync(
        DateTime? startDate,
        DateTime? endDate,
        IReadOnlyList<string>? ispFilter = null,
        IReadOnlyList<string>? productFilter = null,
        IReadOnlyList<string>? categoryFilter = null,
        IReadOnlyList<string>? excludeIsps = null,
        int topN = 10,
        string? groupBy = null
    )
    {
        var exclude = excludeIsps ?? new List<string> { "KTRN" };
        var raw = (await _productSalesRepository.GetProductSalesRawAsync(
            startDate, endDate, exclude, ispFilter, productFilter, categoryFilter
        )).ToList();

        var periods = raw.Select(r => r.Period).Distinct().OrderBy(PeriodOrderKey).ToList();
        var totalPurchases = raw.Sum(r => r.Purchases);
        var periodCount = periods.Count;
        var avgWeekly = periodCount > 0 ? (double)totalPurchases / periodCount : 0;

        var byProduct = raw.GroupBy(r => r.FProdName).ToDictionary(g => g.Key, g => g.Sum(x => x.Purchases));
        var byIsp = raw.GroupBy(r => r.Isp).ToDictionary(g => g.Key, g => g.Sum(x => x.Purchases));
        var byPeriod = raw.GroupBy(r => r.Period).ToDictionary(g => g.Key, g => g.Sum(x => x.Purchases));

        var topProduct = byProduct.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var topIsp = byIsp.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var bestWeek = byPeriod.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var worstWeek = periodCount > 0 ? byPeriod.OrderBy(kv => kv.Value).FirstOrDefault() : default;

        double? wowGrowth = null;
        if (periods.Count >= 2)
        {
            var lastPeriod = periods[periods.Count - 1];
            var prevPeriod = periods[periods.Count - 2];
            var lastTotal = byPeriod.GetValueOrDefault(lastPeriod, 0);
            var prevTotal = byPeriod.GetValueOrDefault(prevPeriod, 0);
            if (prevTotal > 0)
                wowGrowth = Math.Round((double)(lastTotal - prevTotal) / prevTotal * 100, 2);
        }

        var kpis = new ProductSalesKpis
        {
            TotalPurchases = totalPurchases,
            AverageWeeklyPurchases = Math.Round(avgWeekly, 2),
            TopProduct = topProduct.Key ?? "-",
            TopIsp = topIsp.Key ?? "-",
            BestWeek = bestWeek.Key ?? "-",
            WorstWeek = worstWeek.Key ?? "-",
            WeekOverWeekGrowthPercent = wowGrowth,
        };

        // Executive KPI cards: split periods into two halves for comparison
        if (periods.Count >= 2)
        {
            var midpoint = periods.Count / 2;
            var previousPeriods = new HashSet<string>(periods.Take(midpoint));
            var currentPeriods = new HashSet<string>(periods.Skip(midpoint));

            var currentRows = raw.Where(r => currentPeriods.Contains(r.Period)).ToList();
            var previousRows = raw.Where(r => previousPeriods.Contains(r.Period)).ToList();

            var currentRevenue = currentRows.Sum(r => r.RetailRwf);
            var previousRevenue = previousRows.Sum(r => r.RetailRwf);
            var currentUnits = (long)currentRows.Sum(r => r.Purchases);
            var previousUnits = (long)previousRows.Sum(r => r.Purchases);
            var currentRetailers = currentRows.Select(r => r.Isp).Distinct().Count();
            var previousRetailers = previousRows.Select(r => r.Isp).Distinct().Count();

            kpis.Revenue = currentRevenue;
            kpis.PreviousPeriodRevenue = previousRevenue;
            kpis.RevenueGrowthPercent = previousRevenue > 0
                ? Math.Round((double)((currentRevenue - previousRevenue) / previousRevenue) * 100, 2)
                : null;

            kpis.UnitsSold = currentUnits;
            kpis.PreviousPeriodUnitsSold = previousUnits;
            kpis.UnitsGrowthPercent = previousUnits > 0
                ? Math.Round((double)(currentUnits - previousUnits) / previousUnits * 100, 2)
                : null;

            kpis.ActiveRetailers = currentRetailers;
            kpis.PreviousPeriodActiveRetailers = previousRetailers;
            kpis.RetailerGrowthPercent = previousRetailers > 0
                ? Math.Round((double)(currentRetailers - previousRetailers) / previousRetailers * 100, 2)
                : null;

            kpis.OverallGrowthPercent = kpis.RevenueGrowthPercent;

            // Build period labels from the period strings (e.g. "W1 (01/01/26 to 07/01/26)")
            var currentPeriodList = periods.Skip(midpoint).ToList();
            var previousPeriodList = periods.Take(midpoint).ToList();
            kpis.CurrentPeriodLabel = currentPeriodList.Count > 0
                ? $"{currentPeriodList.First()} – {currentPeriodList.Last()}"
                : "";
            kpis.PreviousPeriodLabel = previousPeriodList.Count > 0
                ? $"{previousPeriodList.First()} – {previousPeriodList.Last()}"
                : "";
        }
        else
        {
            // Single period — no comparison possible
            kpis.Revenue = raw.Sum(r => r.RetailRwf);
            kpis.UnitsSold = totalPurchases;
            kpis.ActiveRetailers = raw.Select(r => r.Isp).Distinct().Count();
            kpis.CurrentPeriodLabel = periods.FirstOrDefault() ?? "";
        }

        var groupMode = (groupBy ?? "ISP").ToUpperInvariant();
        var weeklyTrend = BuildWeeklyTrend(raw, periods, groupMode);

        var ispComparison = new List<IspComparisonItem>();
        foreach (var period in periods)
        {
            var periodTotal = raw.Where(r => r.Period == period).Sum(r => r.Purchases);
            foreach (var g in raw.Where(r => r.Period == period).GroupBy(r => r.Isp))
            {
                var purchases = g.Sum(r => r.Purchases);
                var pct = periodTotal > 0 ? Math.Round((double)purchases / periodTotal * 100, 2) : 0;
                ispComparison.Add(new IspComparisonItem
                {
                    Period = period,
                    Isp = g.Key,
                    Purchases = purchases,
                    ContributionPercent = pct,
                });
            }
        }

        var productTotals = raw
            .GroupBy(r => new { r.FProdName, r.Category })
            .Select(g => new { g.Key.FProdName, g.Key.Category, Purchases = g.Sum(x => x.Purchases) })
            .OrderByDescending(x => x.Purchases)
            .Take(Math.Max(1, topN))
            .ToList();
        var topProducts = productTotals.Select(p => new TopProductItem
        {
            ProductName = p.FProdName,
            Category = p.Category,
            Purchases = p.Purchases,
            ProductSharePercent = totalPurchases > 0 ? Math.Round((double)p.Purchases / totalPurchases * 100, 2) : 0,
        }).ToList();

        var heatmapMatrix = raw
            .GroupBy(r => new { r.FProdName, r.Period })
            .Select(g => new HeatmapCell
            {
                Product = g.Key.FProdName,
                Period = g.Key.Period,
                Purchases = g.Sum(x => x.Purchases),
            })
            .ToList();

        var pivotTable = raw
            .GroupBy(r => new { r.Isp, r.FProdName, r.Category })
            .Select(g =>
            {
                var weeks = g.GroupBy(x => x.Period).ToDictionary(x => x.Key, x => x.Sum(r => r.Purchases));
                return new PivotTableRow
                {
                    Isp = g.Key.Isp,
                    Product = g.Key.FProdName,
                    Category = g.Key.Category,
                    TotalPurchases = g.Sum(r => r.Purchases),
                    Weeks = weeks,
                };
            })
            .OrderBy(r => r.Isp).ThenBy(r => r.Product)
            .ToList();

        return new ProductSalesResponse
        {
            Kpis = kpis,
            WeeklyTrend = weeklyTrend,
            IspComparison = ispComparison,
            TopProducts = topProducts,
            HeatmapMatrix = heatmapMatrix,
            PivotTable = pivotTable,
            RawData = raw,
        };
    }

    private static List<WeeklyTrendItem> BuildWeeklyTrend(
        List<ProductSalesRawRow> raw,
        List<string> periods,
        string groupMode
    )
    {
        var list = new List<WeeklyTrendItem>();
        if (groupMode == "WEEK")
        {
            foreach (var period in periods)
            {
                var sum = raw.Where(r => r.Period == period).Sum(r => r.Purchases);
                list.Add(new WeeklyTrendItem
                    { Period = period, GroupKey = "Total", GroupLabel = "Total", Purchases = sum });
            }

            return list;
        }

        if (groupMode == "ISP")
        {
            var isps = raw.Select(r => r.Isp).Distinct().OrderBy(x => x).ToList();
            foreach (var period in periods)
            {
                foreach (var isp in isps)
                {
                    var purchases = raw.Where(r => r.Period == period && r.Isp == isp).Sum(r => r.Purchases);
                    if (purchases > 0)
                        list.Add(new WeeklyTrendItem
                            { Period = period, GroupKey = isp, GroupLabel = isp, Purchases = purchases });
                }
            }

            return list;
        }

        if (groupMode == "PRODUCT")
        {
            var products = raw.Select(r => r.FProdName).Distinct().OrderBy(x => x).ToList();
            foreach (var period in periods)
            {
                foreach (var product in products)
                {
                    var purchases = raw.Where(r => r.Period == period && r.FProdName == product).Sum(r => r.Purchases);
                    if (purchases > 0)
                        list.Add(new WeeklyTrendItem
                            { Period = period, GroupKey = product, GroupLabel = product, Purchases = purchases });
                }
            }

            return list;
        }

        if (groupMode == "CATEGORY")
        {
            var categories = raw.Select(r => r.Category ?? "Other").Distinct().OrderBy(x => x).ToList();
            foreach (var period in periods)
            {
                foreach (var cat in categories)
                {
                    var purchases = raw.Where(r => r.Period == period && (r.Category ?? "Other") == cat)
                        .Sum(r => r.Purchases);
                    if (purchases > 0)
                        list.Add(new WeeklyTrendItem
                            { Period = period, GroupKey = cat, GroupLabel = cat, Purchases = purchases });
                }
            }

            return list;
        }

        return list;
    }

    private static string PeriodOrderKey(string p)
    {
        var m = System.Text.RegularExpressions.Regex.Match(p, @"W(\d+)");
        return m.Success ? m.Groups[1].Value.PadLeft(6, '0') + p : p;
    }
}