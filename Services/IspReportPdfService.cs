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
    Task<byte[]> GenerateTrafficAllIspsPdfAsync(IspReportFilter filter);
    Task<byte[]> GenerateWeeklyMatrixPdfAsync(string? isp, string? weeksFilter);
    Task<byte[]> GenerateProductPivotPdfAsync(string ispName);
    int ClearCache();
    string GetCacheDirectory();
}

public class IspReportPdfService : IIspReportPdfService
{
    private readonly IIspReportRepository _repository;
    private readonly ISalesRepository _salesRepository;
    private readonly IProductSalesRepository _productSalesRepository;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IspReportPdfService> _logger;
    private readonly string _logoPath;

    private static readonly string CacheDir = Path.Combine(
        Path.GetTempPath(),
        "isp-report-pdf-cache"
    );

    public IspReportPdfService(
        IIspReportRepository repository,
        ISalesRepository salesRepository,
        IProductSalesRepository productSalesRepository,
        IWebHostEnvironment env,
        ILogger<IspReportPdfService> logger
    )
    {
        _repository = repository;
        _salesRepository = salesRepository;
        _productSalesRepository = productSalesRepository;
        _env = env;
        _logger = logger;
        _logoPath = Path.Combine(env.ContentRootPath, "Assets", "ktrn-logo.png");
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

    public async Task<byte[]> GenerateTrafficAllIspsPdfAsync(IspReportFilter filter)
    {
        // No heavy caching for traffic initially; reuse monthly-style cache key
        var cacheKey = BuildCacheKey(filter, "traffic");
        var cachedPath = Path.Combine(CacheDir, $"{cacheKey}.pdf");

        if (File.Exists(cachedPath))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
            if (age.TotalDays < 1)
            {
                _logger.LogInformation("Returning cached traffic PDF: {Path}", cachedPath);
                return await File.ReadAllBytesAsync(cachedPath);
            }
        }

        var allSeries = (await _repository.GetTrafficDailyAllIspsAsync(filter)).ToList();

        if (allSeries.Count == 0)
            throw new InvalidOperationException("No traffic data available for the given filter.");

        var combinedPoints = allSeries
            .SelectMany(s => s.Points)
            .GroupBy(p => p.UDay)
            .Select(g => new TrafficReport
            {
                UDay = g.Key,
                Isp = "All ISPs",
                Subs = g.Sum(p => p.Subs),
                UsgGb = g.Sum(p => p.UsgGb),
            })
            .OrderBy(p => p.UDay)
            .ToList();

        var pages = new List<(string Title, List<TrafficReport> Data)>
        {
            ("All ISPs — Traffic Trend", combinedPoints),
        };

        foreach (var series in allSeries.OrderBy(s => s.Isp))
        {
            pages.Add(($"{series.Isp} — Traffic Trend", series.Points.ToList()));
        }

        var pdfBytes = RenderTrafficMultiPagePdf(pages);

        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(cachedPath, pdfBytes);
        _logger.LogInformation("Cached traffic PDF written to: {Path}", cachedPath);

        return pdfBytes;
    }

    public async Task<byte[]> GenerateWeeklyMatrixPdfAsync(string? isp, string? weeksFilter)
    {
        var cacheKeyBase = string.IsNullOrWhiteSpace(isp)
            ? "weekly_matrix_all_isps"
            : $"weekly_matrix_{isp.Replace(" ", "_")}";
        var cacheKey = string.IsNullOrWhiteSpace(weeksFilter)
            ? cacheKeyBase
            : $"{cacheKeyBase}_weeks_{weeksFilter.Replace(",", "-")}";
        var cachedPath = Path.Combine(CacheDir, $"{cacheKey}.pdf");

        if (File.Exists(cachedPath))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachedPath);
            if (age.TotalDays < 1)
            {
                _logger.LogInformation("Returning cached weekly matrix PDF: {Path}", cachedPath);
                return await File.ReadAllBytesAsync(cachedPath);
            }
        }

        var stats = (await _salesRepository.GetWeeklySalesAsync(isp)).ToList();

        if (!string.IsNullOrWhiteSpace(weeksFilter))
        {
            var allowedWeeks = weeksFilter
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedWeeks.Count > 0)
            {
                stats = stats
                    .Where(s =>
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            s.Period ?? string.Empty,
                            "^(W\\d+)"
                        );
                        var weekCode = match.Success ? match.Groups[1].Value : string.Empty;
                        return allowedWeeks.Contains(weekCode);
                    })
                    .ToList();
            }
        }
        if (stats.Count == 0)
        {
            throw new InvalidOperationException("No weekly sales data available for the current period.");
        }

        var pdfBytes = RenderWeeklyMatrixPdf(stats);

        Directory.CreateDirectory(CacheDir);
        await File.WriteAllBytesAsync(cachedPath, pdfBytes);
        _logger.LogInformation("Cached weekly matrix PDF written to: {Path}", cachedPath);

        return pdfBytes;
    }

    public async Task<byte[]> GenerateProductPivotPdfAsync(string ispName)
    {
        if (string.IsNullOrWhiteSpace(ispName))
            throw new ArgumentException("ISP name is required.", nameof(ispName));

        var breakdown = (
            await _productSalesRepository.GetProductWeeklyBreakdownAsync(ispName)
        ).ToList();

        if (breakdown.Count == 0)
            throw new InvalidOperationException(
                $"No product sales data available for ISP: {ispName}."
            );

        return RenderProductPivotPdf(ispName, breakdown);
    }

    private byte[] RenderProductPivotPdf(
        string ispName,
        List<ProductWeeklyBreakdown> breakdown
    )
    {
        const float rowHeight = 18f;

        var periodList = breakdown.Select(s => s.Period).Distinct().ToList();
        var periods = periodList
            .OrderBy(p =>
            {
                var m = System.Text.RegularExpressions.Regex.Match(p ?? "", @"W(\d+)");
                return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
            })
            .ToList();

        var productGroups = breakdown
            .GroupBy(s => s.ProductName)
            .OrderBy(g => g.Key)
            .ToList();

        using var stream = new MemoryStream();
        using var doc = SKDocument.CreatePdf(stream);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };

        using var headerPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };

        using var cellPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 7,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
        };

        using var gridPaint = new SKPaint
        {
            Color = new SKColor(220, 220, 220),
            StrokeWidth = 0.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        int pageNumber = 0;
        SKCanvas? currentCanvas = null;

        SKRect StartNewPage()
        {
            if (currentCanvas != null)
                doc.EndPage();

            pageNumber++;
            bool isCover = pageNumber == 1;

            currentCanvas = doc.BeginPage(PdfBrandingHelper.PageWidth, PdfBrandingHelper.PageHeight);
            return PdfBrandingHelper.DrawPageLayout(
                currentCanvas,
                "Product Pivot Table Report",
                pageNumber,
                isCover,
                _logoPath
            );
        }

        var area = StartNewPage();
        float tableLeft = area.Left;
        float tableRight = area.Right;
        float rowTop = area.Top;

        // Subtitle inside content area
        using var subtitlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };
        var subtitle = $"ISP: {ispName}";
        var subtitleBounds = new SKRect();
        subtitlePaint.MeasureText(subtitle, ref subtitleBounds);
        currentCanvas!.DrawText(subtitle, tableLeft, rowTop + subtitleBounds.Height, subtitlePaint);
        rowTop += subtitleBounds.Height + 16f;

        // Table layout
        float productColWidth = 100f;
        float remainingWidth = tableRight - tableLeft - productColWidth;
        float colWidth = periods.Count > 0 ? remainingWidth / periods.Count : remainingWidth;

        // Measure the tallest rotated header to reserve vertical space
        float maxHeaderTextWidth = 0f;
        foreach (var period in periods)
        {
            var match = System.Text.RegularExpressions.Regex.Match(period ?? "", @"(W\d+)\s*\((.*?)\)");
            if (match.Success)
            {
                var parts = match.Groups[2].Value.Split('-');
                if (parts.Length == 1) parts = match.Groups[2].Value.Split(' ');
                
                foreach (var d in parts)
                {
                    var w = headerPaint.MeasureText(d.Trim());
                    if (w > maxHeaderTextWidth) maxHeaderTextWidth = w;
                }
            }
        }
        if (maxHeaderTextWidth < 20f) maxHeaderTextWidth = 20f;
        float rotatedHeaderHeight = maxHeaderTextWidth + 10f; // Padding
        float weekRowHeight = 24f; // Space for "W1", "W2", etc.
        float totalHeaderHeight = rotatedHeaderHeight + weekRowHeight;

        // Draw column headers (period labels rotated 90° CCW, week num unrotated)
        void DrawTableHeaders(float y)
        {
            // Draw main header box for PRODUCT
            currentCanvas!.DrawRect(new SKRect(tableLeft, y, tableLeft + productColWidth, y + totalHeaderHeight), gridPaint);
            
            // Text for PRODUCT, centered vertically
            var prodText = "PRODUCT";
            var pBounds = new SKRect();
            headerPaint.MeasureText(prodText, ref pBounds);
            currentCanvas.DrawText(prodText, tableLeft + 4, y + (totalHeaderHeight + pBounds.Height) / 2f - 2f, headerPaint);
            
            for (int i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                string weekNum = period;
                string date1 = "";
                string date2 = "";
                
                var match = System.Text.RegularExpressions.Regex.Match(period ?? "", @"(W\d+)\s*\((.*?)\)");
                if (match.Success)
                {
                    weekNum = match.Groups[1].Value;
                    var parsedDates = match.Groups[2].Value
                                           .Split(new[] { " to ", "-", " " }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parsedDates.Length > 0) date1 = parsedDates[0].Trim();
                    if (parsedDates.Length > 1) date2 = parsedDates[parsedDates.Length - 1].Trim(); // skips "to"
                }
                else
                {
                    var pLabel = (period?.Length ?? 0) > 12 ? period!.Substring(0, 12) + "…" : (period ?? "");
                    date1 = pLabel;
                }
                
                float colLeft = tableLeft + productColWidth + i * colWidth;
                float colRight = colLeft + colWidth;
                
                // Draw outer box for the column header
                currentCanvas.DrawRect(new SKRect(colLeft, y, colRight, y + totalHeaderHeight), gridPaint);
                
                // Draw horizontal line separating dates from week number
                currentCanvas.DrawLine(colLeft, y + rotatedHeaderHeight, colRight, y + rotatedHeaderHeight, gridPaint);
                
                // Draw vertical line separating date1 and date2
                float midX = colLeft + colWidth / 2f;
                currentCanvas.DrawLine(midX, y, midX, y + rotatedHeaderHeight, gridPaint);
                
                // Draw Rotated Date 1 (top to bottom)
                if (!string.IsNullOrEmpty(date1))
                {
                    currentCanvas.Save();
                    currentCanvas.Translate(colLeft + colWidth / 4f, y + 4f);
                    currentCanvas.RotateDegrees(90);
                    currentCanvas.DrawText(date1, 0, -(headerPaint.TextSize / 3f), headerPaint);
                    currentCanvas.Restore();
                }
                
                // Draw Rotated Date 2 (top to bottom)
                if (!string.IsNullOrEmpty(date2))
                {
                    currentCanvas.Save();
                    currentCanvas.Translate(colLeft + 3f * colWidth / 4f, y + 4f);
                    currentCanvas.RotateDegrees(90);
                    currentCanvas.DrawText(date2, 0, -(headerPaint.TextSize / 3f), headerPaint);
                    currentCanvas.Restore();
                }
                
                // Draw Week Num centered
                var wBounds = new SKRect();
                headerPaint.MeasureText(weekNum, ref wBounds);
                float wX = colLeft + (colWidth - wBounds.Width) / 2f;
                float wY = y + rotatedHeaderHeight + (weekRowHeight + wBounds.Height) / 2f - 2f;
                currentCanvas.DrawText(weekNum, wX, wY, headerPaint);
            }
        }

        DrawTableHeaders(rowTop);
        rowTop += totalHeaderHeight;

        foreach (var group in productGroups)
        {
            if (rowTop + rowHeight > area.Bottom)
            {
                area = StartNewPage();
                tableLeft = area.Left;
                tableRight = area.Right;
                remainingWidth = tableRight - tableLeft - productColWidth;
                colWidth = periods.Count > 0 ? remainingWidth / periods.Count : remainingWidth;
                rowTop = area.Top;
                DrawTableHeaders(rowTop);
                rowTop += totalHeaderHeight;
            }

            var productName = group.Key;
            var byPeriod = group.ToDictionary(s => s.Period, s => s.Purchases);

            var productLabel = productName.Length > 18 ? productName.Substring(0, 18) + "…" : productName;
            currentCanvas!.DrawRect(new SKRect(tableLeft, rowTop, tableLeft + productColWidth, rowTop + rowHeight), gridPaint);
            currentCanvas.DrawText(productLabel, tableLeft + 4, rowTop + rowHeight - 6f, cellPaint);

            for (int i = 0; i < periods.Count; i++)
            {
                var x = tableLeft + productColWidth + i * colWidth;
                var rect = new SKRect(x, rowTop, x + colWidth, rowTop + rowHeight);
                currentCanvas.DrawRect(rect, gridPaint);

                if (byPeriod.TryGetValue(periods[i], out var purchases))
                {
                    var text = purchases.ToString("N0");
                    currentCanvas.DrawText(text, rect.Left + 4, rect.Bottom - 4, cellPaint);
                }
            }

            rowTop += rowHeight;
        }

        doc.EndPage();
        doc.Close();

        return stream.ToArray();
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

    private byte[] RenderMultiPagePdf(
        List<(string Title, List<IspMonthlyReport> Data)> pages,
        string chartType
    )
    {
        const int chartPixelWidth = 1600;
        const int chartPixelHeight = 900;

        using var stream = new MemoryStream();
        using var skDoc = SKDocument.CreatePdf(stream);

        int pageNumber = 0;
        foreach (var (title, data) in pages)
        {
            pageNumber++;
            bool isCover = pageNumber == 1;

            var plotModel = BuildPlotModel(data, chartType, title);
            using var chartBitmap = RenderChartToBitmap(
                plotModel,
                chartPixelWidth,
                chartPixelHeight
            );

            using var pageCanvas = skDoc.BeginPage(PdfBrandingHelper.PageWidth, PdfBrandingHelper.PageHeight);

            var contentArea = PdfBrandingHelper.DrawPageLayout(
                pageCanvas,
                "Prepaid Sales Report",
                pageNumber,
                isCover,
                _logoPath
            );

            // Fit chart image into content area
            var scale = Math.Min(
                contentArea.Width / chartBitmap.Width,
                contentArea.Height / chartBitmap.Height
            );

            var imgWidth = chartBitmap.Width * scale;
            var imgHeight = chartBitmap.Height * scale;
            var x = contentArea.Left + (contentArea.Width - imgWidth) / 2;
            var y = contentArea.Top + (contentArea.Height - imgHeight) / 2;

            pageCanvas.DrawBitmap(
                chartBitmap,
                SKRect.Create(x, y, imgWidth, imgHeight)
            );

            skDoc.EndPage();
        }

        skDoc.Close();
        return stream.ToArray();
    }

    private byte[] RenderTrafficMultiPagePdf(
        List<(string Title, List<TrafficReport> Data)> pages
    )
    {
        const int chartPixelWidth = 1600;
        const int chartPixelHeight = 900;

        using var stream = new MemoryStream();
        using var skDoc = SKDocument.CreatePdf(stream);

        int pageNumber = 0;
        foreach (var (title, data) in pages)
        {
            pageNumber++;
            bool isCover = pageNumber == 1;

            var plotModel = BuildTrafficPlotModel(data, title);
            using var chartBitmap = RenderChartToBitmap(
                plotModel,
                chartPixelWidth,
                chartPixelHeight
            );

            using var pageCanvas = skDoc.BeginPage(PdfBrandingHelper.PageWidth, PdfBrandingHelper.PageHeight);

            var contentArea = PdfBrandingHelper.DrawPageLayout(
                pageCanvas,
                "Traffic Report",
                pageNumber,
                isCover,
                _logoPath
            );

            // Fit chart image into content area
            var scale = Math.Min(
                contentArea.Width / chartBitmap.Width,
                contentArea.Height / chartBitmap.Height
            );

            var imgWidth = chartBitmap.Width * scale;
            var imgHeight = chartBitmap.Height * scale;
            var x = contentArea.Left + (contentArea.Width - imgWidth) / 2;
            var y = contentArea.Top + (contentArea.Height - imgHeight) / 2;

            pageCanvas.DrawBitmap(
                chartBitmap,
                SKRect.Create(x, y, imgWidth, imgHeight)
            );

            skDoc.EndPage();
        }

        skDoc.Close();
        return stream.ToArray();
    }

    private byte[] RenderWeeklyMatrixPdf(List<WeeklySalesStat> stats)
    {
        const float rowHeight = 24f;

        var periods = stats
            .Select(s => s.Period)
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var ispGroups = stats
            .GroupBy(s => s.Isp)
            .OrderBy(g => g.Key)
            .ToList();

        using var stream = new MemoryStream();
        using var doc = SKDocument.CreatePdf(stream);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 14,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };

        using var headerPaint = new SKPaint
        {
            Color = SKColors.Green,
            TextSize = 8,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };

        using var cellPaint = new SKPaint
        {
            Color = SKColors.Black,
            TextSize = 7,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
        };

        using var gridPaint = new SKPaint
        {
            Color = new SKColor(220, 220, 220),
            StrokeWidth = 0.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        // Build week date labels for subtitle
        var weekDateLabels = periods
            .Select(p =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    p ?? string.Empty,
                    "\\(([^)]+)\\)"
                );
                return match.Success ? match.Groups[1].Value : string.Empty;
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        int pageNumber = 0;
        SKCanvas? currentCanvas = null;

        // Helper to start a new page and draw headers + table column headers
        SKRect StartNewPage()
        {
            if (currentCanvas != null)
                doc.EndPage();

            pageNumber++;
            bool isCover = pageNumber == 1;

            currentCanvas = doc.BeginPage(PdfBrandingHelper.PageWidth, PdfBrandingHelper.PageHeight);
            var contentArea = PdfBrandingHelper.DrawPageLayout(
                currentCanvas,
                "ISP × Week Sales Matrix",
                pageNumber,
                isCover,
                _logoPath
            );

            return contentArea;
        }

        // Start first page
        var area = StartNewPage();
        float tableLeft = area.Left;
        float tableRight = area.Right;
        float rowTop = area.Top;

        // Subtitle inside content area (first page only)
        if (weekDateLabels.Count > 0)
        {
            using var subtitlePaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 14,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
            };
            var subtitle = $"Weeks: {string.Join("; ", weekDateLabels)}";
            var subtitleBounds = new SKRect();
            subtitlePaint.MeasureText(subtitle, ref subtitleBounds);
            currentCanvas!.DrawText(subtitle, tableLeft, rowTop + subtitleBounds.Height, subtitlePaint);
            rowTop += subtitleBounds.Height + 16f;
        }

        // Table layout
        float ispColWidth = 80f;
        float totalColWidth = 55f;
        float remainingWidth = tableRight - tableLeft - ispColWidth - totalColWidth;
        float colWidth = periods.Count > 0 ? remainingWidth / periods.Count : remainingWidth;

        // Draw column headers
        void DrawTableHeaders(float y)
        {
            currentCanvas!.DrawText("ISP", tableLeft + 4, y, headerPaint);
            currentCanvas.DrawText("Total", tableLeft + ispColWidth + 4, y, headerPaint);
            for (int i = 0; i < periods.Count; i++)
            {
                var period = periods[i];
                var x = tableLeft + ispColWidth + totalColWidth + i * colWidth + 4;
                var label = period.Split(' ')[0];
                currentCanvas.DrawText(label, x, y, headerPaint);
            }
        }

        DrawTableHeaders(rowTop);
        rowTop += 12f;

        foreach (var group in ispGroups)
        {
            if (rowTop + rowHeight > area.Bottom)
            {
                area = StartNewPage();
                tableLeft = area.Left;
                tableRight = area.Right;
                remainingWidth = tableRight - tableLeft - ispColWidth - totalColWidth;
                colWidth = periods.Count > 0 ? remainingWidth / periods.Count : remainingWidth;
                rowTop = area.Top;
                DrawTableHeaders(rowTop);
                rowTop += 12f;
            }

            var ispName = group.Key;
            var totalPurchases = group.Sum(s => s.Purchases);

            currentCanvas!.DrawText(ispName, tableLeft + 4, rowTop + rowHeight - 4, cellPaint);
            currentCanvas.DrawText(
                totalPurchases.ToString("N0"),
                tableLeft + ispColWidth + 4,
                rowTop + rowHeight - 4,
                cellPaint
            );

            var byPeriod = group.ToDictionary(s => s.Period, s => s);

            for (int i = 0; i < periods.Count; i++)
            {
                var x = tableLeft + ispColWidth + totalColWidth + i * colWidth;
                var rect = new SKRect(
                    x,
                    rowTop,
                    x + colWidth,
                    rowTop + rowHeight
                );

                currentCanvas.DrawRect(rect, gridPaint);

                if (byPeriod.TryGetValue(periods[i], out var cell))
                {
                    var text = cell.Purchases.ToString("N0");
                    currentCanvas.DrawText(
                        text,
                        rect.Left + 4,
                        rect.Bottom - 4,
                        cellPaint
                    );
                }
            }

            rowTop += rowHeight;
        }

        doc.EndPage();
        doc.Close();

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
        var purchaseColor = OxyColors.Red; // #FF0000
        var amountColor = OxyColors.Black; // #000000

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

    private static PlotModel BuildTrafficPlotModel(
        List<TrafficReport> data,
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

        var days = data.Select(d => FormatDay(d.UDay)).ToArray();
        var subsColor = OxyColor.FromRgb(0x22, 0xC5, 0x5E); // Green #22C55E
        var trafficColor = OxyColors.Red; // #FF0000

        var categoryAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            ItemsSource = days,
            GapWidth = 0.2,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            Angle = -45,
            FontSize = 10,
        };
        model.Axes.Add(categoryAxis);

        var hasData = data.Count > 0;
        var minSubs = hasData ? data.Min(d => (double)d.Subs) : 0d;
        var minTraffic = hasData ? data.Min(d => (double)d.UsgGb) : 0d;
        var globalMin = Math.Min(minSubs, minTraffic);

        var subsAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "subs",
            Title = "Subscriptions",
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(229, 231, 235),
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            StringFormat = "#,0",
            FontSize = 10,
            Minimum = hasData ? globalMin : double.NaN,
        };
        model.Axes.Add(subsAxis);

        var trafficAxis = new LinearAxis
        {
            Position = AxisPosition.Right,
            Key = "traffic",
            Title = "Traffic (GB)",
            TitleFontSize = 11,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            StringFormat = "#,0",
            FontSize = 10,
            Minimum = hasData ? globalMin : double.NaN,
        };
        model.Axes.Add(trafficAxis);

        var subsSeries = new AreaSeries
        {
            Title = "Subscriptions",
            YAxisKey = "subs",
            Color = subsColor,
            Fill = OxyColor.FromAColor(50, subsColor),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
        };

        var trafficSeries = new AreaSeries
        {
            Title = "Traffic (GB)",
            YAxisKey = "traffic",
            Color = trafficColor,
            Fill = OxyColor.FromAColor(50, trafficColor),
            StrokeThickness = 2,
            MarkerType = MarkerType.None,
        };

        for (int i = 0; i < data.Count; i++)
        {
            subsSeries.Points.Add(new DataPoint(i, data[i].Subs));
            trafficSeries.Points.Add(new DataPoint(i, (double)data[i].UsgGb));
        }

        model.Series.Add(subsSeries);
        model.Series.Add(trafficSeries);

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

    private byte[] RenderPostpaidMultiPagePdf(
        List<(string Title, List<PostpaidReport> Data)> pages,
        string chartType
    )
    {
        const int chartPixelWidth = 1600;
        const int chartPixelHeight = 900;

        using var stream = new MemoryStream();
        using var skDoc = SKDocument.CreatePdf(stream);

        int pageNumber = 0;
        foreach (var (title, data) in pages)
        {
            pageNumber++;
            bool isCover = pageNumber == 1;

            var plotModel = BuildPostpaidPlotModel(data, chartType, title);
            using var chartBitmap = RenderChartToBitmap(
                plotModel,
                chartPixelWidth,
                chartPixelHeight
            );

            using var pageCanvas = skDoc.BeginPage(PdfBrandingHelper.PageWidth, PdfBrandingHelper.PageHeight);

            var contentArea = PdfBrandingHelper.DrawPageLayout(
                pageCanvas,
                "Postpaid Sales Report",
                pageNumber,
                isCover,
                _logoPath
            );

            // Fit chart image into content area
            var scale = Math.Min(
                contentArea.Width / chartBitmap.Width,
                contentArea.Height / chartBitmap.Height
            );

            var imgWidth = chartBitmap.Width * scale;
            var imgHeight = chartBitmap.Height * scale;
            var x = contentArea.Left + (contentArea.Width - imgWidth) / 2;
            var y = contentArea.Top + (contentArea.Height - imgHeight) / 2;

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
        var eWalletColor = OxyColor.FromRgb(0x22, 0xC5, 0x5E); // Green #22C55E

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

    private static string FormatDay(string yyyymmdd)
    {
        if (yyyymmdd.Length != 8)
            return yyyymmdd;

        return $"{yyyymmdd.Substring(0, 4)}-{yyyymmdd.Substring(4, 2)}-{yyyymmdd.Substring(6, 2)}";
    }
}
