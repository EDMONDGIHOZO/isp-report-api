using isp_report_api.Models;
using isp_report_api.Repository;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.SkiaSharp;
using SkiaSharp;

namespace isp_report_api.Services;

public interface IIspReportPdfService
{
    Task<byte[]> GenerateAllIspsPdfAsync(IspReportFilter filter, string chartType);
    Task<byte[]> GeneratePostpaidAllIspsPdfAsync(IspReportFilter filter, string chartType);
    int ClearCache();
    string GetCacheDirectory();
}

public class IspReportPdfService : IIspReportPdfService
{
    private readonly IIspReportRepository _repository;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IspReportPdfService> _logger;

    private static readonly string CacheDir = Path.Combine(
        Path.GetTempPath(),
        "isp-report-pdf-cache"
    );

    public IspReportPdfService(
        IIspReportRepository repository,
        IWebHostEnvironment env,
        ILogger<IspReportPdfService> logger
    )
    {
        _repository = repository;
        _env = env;
        _logger = logger;
    }

    public async Task<byte[]> GenerateAllIspsPdfAsync(
        IspReportFilter filter,
        string chartType
    )
    {
        // Build a cache key from filter + chart type
        var cacheKey = BuildCacheKey(filter, chartType);
        var cachedPath = Path.Combine(CacheDir, $"{cacheKey}.pdf");

        // Return cached file if still valid (< 1 day old)
        if (File.Exists(cachedPath))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
            if (age.TotalDays < 1)
            {
                _logger.LogInformation("Returning cached PDF: {Path}", cachedPath);
                return await File.ReadAllBytesAsync(cachedPath);
            }
        }

        // Fetch data for all ISPs
        var allSeries = (
            await _repository.GetMonthlyReportsAllIspsAsync(filter)
        ).ToList();

        if (allSeries.Count == 0)
            throw new InvalidOperationException("No data available for the given filter.");

        // Build combined "All ISPs" data by summing across ISPs per month
        var combinedPoints = allSeries
            .SelectMany(s => s.Points)
            .GroupBy(p => p.UDay)
            .Select(g => new IspMonthlyReport
            {
                UDay = g.Key,
                Purchase = g.Sum(p => p.Purchase),
                Amount = g.Sum(p => p.Amount),
            })
            .OrderBy(p => p.UDay)
            .ToList();

        // Build pages: first page = "All ISPs", then one per ISP
        var pages = new List<(string Title, List<IspMonthlyReport> Data)>
        {
            ("All ISPs — Prepaid Sales Trend", combinedPoints),
        };

        foreach (var series in allSeries.OrderBy(s => s.Isp))
        {
            pages.Add(($"{series.Isp} — Prepaid Sales Trend", series.Points.ToList()));
        }

        // Generate multi-page PDF
        var pdfBytes = RenderMultiPagePdf(pages, chartType);

        // Cache the result
        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(cachedPath, pdfBytes);
        _logger.LogInformation("Cached PDF written to: {Path}", cachedPath);

        return pdfBytes;
    }

    public async Task<byte[]> GeneratePostpaidAllIspsPdfAsync(
        IspReportFilter filter,
        string chartType
    )
    {
        // Only cache if no custom filtering (no ISP filter, default date range)
        var shouldCache = ShouldCachePostpaid(filter);
        string? cacheKey = null;
        string? cachedPath = null;

        if (shouldCache)
        {
            cacheKey = BuildPostpaidCacheKey(chartType);
            cachedPath = Path.Combine(CacheDir, $"{cacheKey}.pdf");

            // Return cached file if still valid (< 1 day old)
            if (File.Exists(cachedPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
                if (age.TotalDays < 1)
                {
                    _logger.LogInformation("Returning cached postpaid PDF: {Path}", cachedPath);
                    return await File.ReadAllBytesAsync(cachedPath);
                }
            }
        }

        // Fetch data for all ISPs
        var allSeries = (
            await _repository.GetPostpaidReportsAllIspsAsync(filter)
        ).ToList();

        if (allSeries.Count == 0)
            throw new InvalidOperationException("No data available for the given filter.");

        // Build combined "All ISPs" data by summing across ISPs per month
        var combinedPoints = allSeries
            .SelectMany(s => s.Points)
            .GroupBy(p => p.Period)
            .Select(g => new PostpaidReport
            {
                Period = g.Key,
                Isp = "All ISPs",
                EWallet = g.Sum(p => p.EWallet),
            })
            .OrderBy(p => p.Period)
            .ToList();

        // Build pages: first page = "All ISPs", then one per ISP
        var pages = new List<(string Title, List<PostpaidReport> Data)>
        {
            ("All ISPs — Postpaid Sales Trend", combinedPoints),
        };

        foreach (var series in allSeries.OrderBy(s => s.Isp))
        {
            pages.Add(($"{series.Isp} — Postpaid Sales Trend", series.Points.ToList()));
        }

        // Generate multi-page PDF
        var pdfBytes = RenderPostpaidMultiPagePdf(pages, chartType);

        // Cache the result only if no custom filtering
        if (shouldCache && cacheKey != null && cachedPath != null)
        {
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(cachedPath, pdfBytes);
            _logger.LogInformation("Cached postpaid PDF written to: {Path}", cachedPath);
        }
        else
        {
            _logger.LogInformation("Skipping cache for postpaid PDF (custom filters detected)");
        }

        return pdfBytes;
    }

    private static bool ShouldCachePostpaid(IspReportFilter filter)
    {
        // Only cache if:
        // 1. No ISP filter
        // 2. No custom date range (both FromPeriod and ToPeriod are null/default)
        return string.IsNullOrEmpty(filter.IspName)
            && string.IsNullOrEmpty(filter.FromPeriod)
            && string.IsNullOrEmpty(filter.ToPeriod);
    }

    public int ClearCache()
    {
        if (!Directory.Exists(CacheDir))
            return 0;

        var files = Directory.GetFiles(CacheDir, "*.pdf");
        foreach (var file in files)
            File.Delete(file);

        _logger.LogInformation("Cleared {Count} cached PDF(s) from {Dir}", files.Length, CacheDir);
        return files.Length;
    }

    public string GetCacheDirectory() => CacheDir;

    /* ------------------------------------------------------------------ */
    /* PDF rendering using SkiaSharp SKDocument                            */
    /* ------------------------------------------------------------------ */

    private static byte[] RenderMultiPagePdf(
        List<(string Title, List<IspMonthlyReport> Data)> pages,
        string chartType
    )
    {
        const float pageWidth = 842; // A4 landscape width in points
        const float pageHeight = 595; // A4 landscape height in points
        const int chartPixelWidth = 1600;
        const int chartPixelHeight = 900;

        using var stream = new MemoryStream();
        using var skDoc = SKDocument.CreatePdf(stream);

        foreach (var (title, data) in pages)
        {
            var plotModel = BuildPlotModel(data, chartType, title);
            using var chartBitmap = RenderChartToBitmap(
                plotModel,
                chartPixelWidth,
                chartPixelHeight
            );

            using var pageCanvas = skDoc.BeginPage(pageWidth, pageHeight);

            // White background
            pageCanvas.Clear(SKColors.White);

            // Fit chart image into page with margin
            const float margin = 30f;
            var availableWidth = pageWidth - 2 * margin;
            var availableHeight = pageHeight - 2 * margin;

            var scale = Math.Min(
                availableWidth / chartBitmap.Width,
                availableHeight / chartBitmap.Height
            );

            var imgWidth = chartBitmap.Width * scale;
            var imgHeight = chartBitmap.Height * scale;
            var x = (pageWidth - imgWidth) / 2;
            var y = (pageHeight - imgHeight) / 2;

            pageCanvas.DrawBitmap(
                chartBitmap,
                SKRect.Create(x, y, imgWidth, imgHeight)
            );

            skDoc.EndPage();
        }

        skDoc.Close();
        return stream.ToArray();
    }

    private static SKBitmap RenderChartToBitmap(
        PlotModel model,
        int width,
        int height
    )
    {
        var exporter = new PngExporter { Width = width, Height = height };

        using var ms = new MemoryStream();
        exporter.Export(model, ms);

        ms.Position = 0;
        return SKBitmap.Decode(ms);
    }

    /* ------------------------------------------------------------------ */
    /* OxyPlot model construction                                         */
    /* ------------------------------------------------------------------ */

    private static PlotModel BuildPlotModel(
        List<IspMonthlyReport> data,
        string chartType,
        string title
    )
    {
        var model = new PlotModel
        {
            Title = title,
            TitleFontSize = 16,
            TitleFontWeight = FontWeights.Bold,
            PlotAreaBorderThickness = new OxyThickness(0),
            Padding = new OxyThickness(10, 20, 30, 10),
        };

        model.PlotAreaBorderColor = OxyColors.Transparent;

        var months = data.Select(d => FormatMonth(d.UDay)).ToArray();
        var purchaseColor = OxyColor.FromRgb(0x3C, 0x71, 0xDD); // #3c71dd
        var amountColor = OxyColor.FromRgb(0x79, 0x53, 0xC6); // #7953c6

        var isBar = chartType.Equals("bar", StringComparison.OrdinalIgnoreCase);

        if (isBar)
        {
            // Bar chart: use LinearAxis on X so RectangleAnnotation coordinates work
            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -0.5,
                Maximum = months.Length - 0.5,
                MajorStep = 1,
                MinorStep = 1,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TickStyle = TickStyle.None,
                Angle = -45,
                FontSize = 10,
                LabelFormatter = val =>
                {
                    var idx = (int)Math.Round(val);
                    return idx >= 0 && idx < months.Length ? months[idx] : "";
                },
            };
            model.Axes.Add(xAxis);
        }
        else
        {
            // Area / Line: CategoryAxis on X
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = months,
                GapWidth = 0.2,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TickStyle = TickStyle.None,
                Angle = -45,
                FontSize = 10,
            };
            model.Axes.Add(categoryAxis);
        }

        var purchaseAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "purchase",
            Title = "Purchases",
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(229, 231, 235),
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            StringFormat = "#,0",
            FontSize = 10,
            Minimum = 0,
        };
        model.Axes.Add(purchaseAxis);

        var amountAxis = new LinearAxis
        {
            Position = AxisPosition.Right,
            Key = "amount",
            Title = "Amount (RWF)",
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            StringFormat = "#,0",
            FontSize = 10,
            Minimum = 0,
        };
        model.Axes.Add(amountAxis);

        switch (chartType.ToLowerInvariant())
        {
            case "bar":
                AddVerticalBarAnnotations(model, data, purchaseColor, amountColor);
                break;
            case "line":
                AddLineSeries(model, data, purchaseColor, amountColor);
                break;
            case "area":
            default:
                AddAreaSeries(model, data, purchaseColor, amountColor);
                break;
        }

        return model;
    }

    private static void AddAreaSeries(
        PlotModel model,
        List<IspMonthlyReport> data,
        OxyColor purchaseColor,
        OxyColor amountColor
    )
    {
        var purchaseSeries = new AreaSeries
        {
            Title = "Purchases",
            YAxisKey = "purchase",
            Color = purchaseColor,
            Fill = OxyColor.FromAColor(50, purchaseColor),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
        };

        var amountSeries = new AreaSeries
        {
            Title = "Amount (RWF)",
            YAxisKey = "amount",
            Color = amountColor,
            Fill = OxyColor.FromAColor(50, amountColor),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
        };

        for (int i = 0; i < data.Count; i++)
        {
            purchaseSeries.Points.Add(new DataPoint(i, data[i].Purchase));
            amountSeries.Points.Add(new DataPoint(i, (double)data[i].Amount));
        }

        model.Series.Add(purchaseSeries);
        model.Series.Add(amountSeries);
    }

    private static void AddLineSeries(
        PlotModel model,
        List<IspMonthlyReport> data,
        OxyColor purchaseColor,
        OxyColor amountColor
    )
    {
        var purchaseSeries = new LineSeries
        {
            Title = "Purchases",
            YAxisKey = "purchase",
            Color = purchaseColor,
            StrokeThickness = 2,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3,
            MarkerFill = purchaseColor,
        };

        var amountSeries = new LineSeries
        {
            Title = "Amount (RWF)",
            YAxisKey = "amount",
            Color = amountColor,
            StrokeThickness = 2,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3,
            MarkerFill = amountColor,
        };

        for (int i = 0; i < data.Count; i++)
        {
            purchaseSeries.Points.Add(new DataPoint(i, data[i].Purchase));
            amountSeries.Points.Add(new DataPoint(i, (double)data[i].Amount));
        }

        model.Series.Add(purchaseSeries);
        model.Series.Add(amountSeries);
    }

    private static void AddVerticalBarAnnotations(
        PlotModel model,
        List<IspMonthlyReport> data,
        OxyColor purchaseColor,
        OxyColor amountColor
    )
    {
        const double barHalfWidth = 0.35;
        const double gap = 0.02;

        // Annotations don't contribute to axis auto-range,
        // so we must set explicit axis limits from the data.
        var maxPurchase = data.Max(d => (double)d.Purchase);
        var maxAmount = data.Max(d => (double)d.Amount);

        foreach (var axis in model.Axes)
        {
            if (axis.Key == "purchase" && axis is LinearAxis pa)
                pa.Maximum = maxPurchase * 1.15;
            else if (axis.Key == "amount" && axis is LinearAxis aa)
                aa.Maximum = maxAmount * 1.15;
        }

        // Invisible series for legend display only.
        // We add one off-screen point so OxyPlot acknowledges the series exists.
        var purchaseLegend = new LineSeries
        {
            Title = "Purchases",
            YAxisKey = "purchase",
            Color = OxyColors.Transparent,
            StrokeThickness = 0,
            MarkerType = MarkerType.Square,
            MarkerSize = 10,
            MarkerFill = purchaseColor,
            MarkerStroke = OxyColors.Transparent,
        };
        purchaseLegend.Points.Add(new DataPoint(-10, -10));
        model.Series.Add(purchaseLegend);

        var amountLegend = new LineSeries
        {
            Title = "Amount (RWF)",
            YAxisKey = "amount",
            Color = OxyColors.Transparent,
            StrokeThickness = 0,
            MarkerType = MarkerType.Square,
            MarkerSize = 10,
            MarkerFill = amountColor,
            MarkerStroke = OxyColors.Transparent,
        };
        amountLegend.Points.Add(new DataPoint(-10, -10));
        model.Series.Add(amountLegend);

        for (int i = 0; i < data.Count; i++)
        {
            // Purchase bar — left side of the group
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = i - barHalfWidth,
                MaximumX = i - gap,
                MinimumY = 0,
                MaximumY = data[i].Purchase,
                Fill = purchaseColor,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0,
                YAxisKey = "purchase",
            });

            // Amount bar — right side of the group
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = i + gap,
                MaximumX = i + barHalfWidth,
                MinimumY = 0,
                MaximumY = (double)data[i].Amount,
                Fill = amountColor,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0,
                YAxisKey = "amount",
            });
        }
    }

    /* ------------------------------------------------------------------ */
    /* Helpers                                                            */
    /* ------------------------------------------------------------------ */

    private static string BuildCacheKey(IspReportFilter filter, string chartType)
    {
        var from = filter.FromPeriod ?? "default";
        var to = filter.ToPeriod ?? "default";
        return $"all-isps_{from}_{to}_{chartType.ToLowerInvariant()}";
    }

    private static string BuildPostpaidCacheKey(string chartType)
    {
        return $"postpaid-all-isps_default_{chartType.ToLowerInvariant()}";
    }

    private static byte[] RenderPostpaidMultiPagePdf(
        List<(string Title, List<PostpaidReport> Data)> pages,
        string chartType
    )
    {
        const float pageWidth = 842; // A4 landscape width in points
        const float pageHeight = 595; // A4 landscape height in points
        const int chartPixelWidth = 1600;
        const int chartPixelHeight = 900;

        using var stream = new MemoryStream();
        using var skDoc = SKDocument.CreatePdf(stream);

        foreach (var (title, data) in pages)
        {
            var plotModel = BuildPostpaidPlotModel(data, chartType, title);
            using var chartBitmap = RenderChartToBitmap(
                plotModel,
                chartPixelWidth,
                chartPixelHeight
            );

            using var pageCanvas = skDoc.BeginPage(pageWidth, pageHeight);

            // White background
            pageCanvas.Clear(SKColors.White);

            // Fit chart image into page with margin
            const float margin = 30f;
            var availableWidth = pageWidth - 2 * margin;
            var availableHeight = pageHeight - 2 * margin;

            var scale = Math.Min(
                availableWidth / chartBitmap.Width,
                availableHeight / chartBitmap.Height
            );

            var imgWidth = chartBitmap.Width * scale;
            var imgHeight = chartBitmap.Height * scale;
            var x = (pageWidth - imgWidth) / 2;
            var y = (pageHeight - imgHeight) / 2;

            pageCanvas.DrawBitmap(
                chartBitmap,
                SKRect.Create(x, y, imgWidth, imgHeight)
            );

            skDoc.EndPage();
        }

        skDoc.Close();
        return stream.ToArray();
    }

    private static PlotModel BuildPostpaidPlotModel(
        List<PostpaidReport> data,
        string chartType,
        string title
    )
    {
        var model = new PlotModel
        {
            Title = title,
            TitleFontSize = 16,
            TitleFontWeight = FontWeights.Bold,
            PlotAreaBorderThickness = new OxyThickness(0),
            Padding = new OxyThickness(10, 20, 30, 10),
        };

        model.PlotAreaBorderColor = OxyColors.Transparent;

        var months = data.Select(d => FormatMonth(d.Period)).ToArray();
        var eWalletColor = OxyColor.FromRgb(0x3C, 0x71, 0xDD); // #3c71dd

        var isBar = chartType.Equals("bar", StringComparison.OrdinalIgnoreCase);

        if (isBar)
        {
            // Bar chart: use LinearAxis on X
            var xAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -0.5,
                Maximum = months.Length - 0.5,
                MajorStep = 1,
                MinorStep = 1,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TickStyle = TickStyle.None,
                Angle = -45,
                FontSize = 10,
                LabelFormatter = val =>
                {
                    var idx = (int)Math.Round(val);
                    return idx >= 0 && idx < months.Length ? months[idx] : "";
                },
            };
            model.Axes.Add(xAxis);
        }
        else
        {
            // Line: CategoryAxis on X
            var categoryAxis = new CategoryAxis
            {
                Position = AxisPosition.Bottom,
                ItemsSource = months,
                GapWidth = 0.2,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                TickStyle = TickStyle.None,
                Angle = -45,
                FontSize = 10,
            };
            model.Axes.Add(categoryAxis);
        }

        var eWalletAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "EWallet Amount (RWF)",
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(229, 231, 235),
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            StringFormat = "#,0",
            FontSize = 10,
            Minimum = 0,
        };
        model.Axes.Add(eWalletAxis);

        switch (chartType.ToLowerInvariant())
        {
            case "bar":
                AddPostpaidBarSeries(model, data, eWalletColor);
                break;
            case "line":
            default:
                AddPostpaidLineSeries(model, data, eWalletColor);
                break;
        }

        return model;
    }

    private static void AddPostpaidLineSeries(
        PlotModel model,
        List<PostpaidReport> data,
        OxyColor eWalletColor
    )
    {
        var eWalletSeries = new LineSeries
        {
            Title = "EWallet Amount (RWF)",
            Color = eWalletColor,
            StrokeThickness = 2,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3,
            MarkerFill = eWalletColor,
        };

        for (int i = 0; i < data.Count; i++)
        {
            eWalletSeries.Points.Add(new DataPoint(i, (double)data[i].EWallet));
        }

        model.Series.Add(eWalletSeries);
    }

    private static void AddPostpaidBarSeries(
        PlotModel model,
        List<PostpaidReport> data,
        OxyColor eWalletColor
    )
    {
        const double barHalfWidth = 0.4;

        // Set explicit axis limits from the data
        var maxEWallet = data.Max(d => (double)d.EWallet);

        foreach (var axis in model.Axes)
        {
            if (axis is LinearAxis la && axis.Position == AxisPosition.Left)
                la.Maximum = maxEWallet * 1.15;
        }

        // Invisible series for legend display only
        var eWalletLegend = new LineSeries
        {
            Title = "EWallet Amount (RWF)",
            Color = OxyColors.Transparent,
            StrokeThickness = 0,
            MarkerType = MarkerType.Square,
            MarkerSize = 10,
            MarkerFill = eWalletColor,
            MarkerStroke = OxyColors.Transparent,
        };
        eWalletLegend.Points.Add(new DataPoint(-10, -10));
        model.Series.Add(eWalletLegend);

        for (int i = 0; i < data.Count; i++)
        {
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = i - barHalfWidth,
                MaximumX = i + barHalfWidth,
                MinimumY = 0,
                MaximumY = (double)data[i].EWallet,
                Fill = eWalletColor,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0,
            });
        }
    }

    private static readonly string[] MonthNames =
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
    };

    private static string FormatMonth(string yyyymm)
    {
        if (yyyymm.Length < 6)
            return yyyymm;
        var monthIdx = int.Parse(yyyymm.Substring(4, 2)) - 1;
        if (monthIdx < 0 || monthIdx > 11)
            return yyyymm;
        return $"{MonthNames[monthIdx]} {yyyymm[..4]}";
    }
}
