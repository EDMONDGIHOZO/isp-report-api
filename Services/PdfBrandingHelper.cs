using SkiaSharp;

namespace isp_report_api.Services;

/// <summary>
/// Shared helper for branded PDF page layout.
///
/// Page 1 (cover):
///   ┌──────────────────────────────────┐
///   │ DARK HEADER  logo ··· DD-MM-YY   │  60 pt
///   │ RED TITLE BAR  "Document Title"  │  30 pt
///   │ ┌──────────────────────────────┐ │
///   │ │                              │ │
///   │ │        CONTENT AREA          │ │
///   │ │                              │ │
///   │ └──────────────────────────────┘ │
///   │          PAGE 1                  │  20 pt
///   └──────────────────────────────────┘
///
/// Page 2+ (continuation):
///   ┌──────────────────────────────────┐
///   │ SLIM DARK HEADER   "KTRN"       │  30 pt
///   │ ┌──────────────────────────────┐ │
///   │ │                              │ │
///   │ │        CONTENT AREA          │ │
///   │ │                              │ │
///   │ └──────────────────────────────┘ │
///   │          PAGE N                  │  20 pt
///   └──────────────────────────────────┘
/// </summary>
public static class PdfBrandingHelper
{
    // A4 Portrait in points (1 pt = 1/72 inch)
    public const float PageWidth = 595f;
    public const float PageHeight = 842f;

    // Layout constants
    private const float Margin = 30f;
    private const float CoverHeaderHeight = 60f;
    private const float CoverTitleBarHeight = 30f;
    private const float CoverHeaderTotal = CoverHeaderHeight + CoverTitleBarHeight;
    private const float ContinuationHeaderHeight = 30f;
    private const float FooterHeight = 25f;
    private const float ContentPadding = 10f;

    // Colors
    private static readonly SKColor DarkBg = new(0x2D, 0x2D, 0x2D);
    private static readonly SKColor RedBg = new(0xD3, 0x2F, 0x2F);
    private static readonly SKColor White = SKColors.White;
    private static readonly SKColor Black = SKColors.Black;
    private static readonly SKColor BorderColor = new(0x33, 0x33, 0x33);

    // Cached logo bitmap (loaded once)
    private static SKBitmap? _logoBitmap;
    private static readonly object LogoLock = new();

    /// <summary>
    /// Returns the content area rectangle for a given page.
    /// This is the usable area inside the border where content should be drawn.
    /// </summary>
    public static SKRect GetContentArea(bool isCoverPage)
    {
        float top = isCoverPage
            ? CoverHeaderTotal + Margin
            : ContinuationHeaderHeight + Margin / 2;

        float bottom = PageHeight - FooterHeight - Margin / 2;
        float left = Margin;
        float right = PageWidth - Margin;

        return new SKRect(left, top, right, bottom);
    }

    /// <summary>
    /// Draws the full branded layout for a page and returns the content area rect.
    /// </summary>
    public static SKRect DrawPageLayout(
        SKCanvas canvas,
        string documentTitle,
        int pageNumber,
        bool isCoverPage,
        string? logoPath = null)
    {
        canvas.Clear(White);

        if (isCoverPage)
            DrawCoverHeader(canvas, documentTitle, logoPath);
        else
            DrawContinuationHeader(canvas);

        DrawFooter(canvas, pageNumber);

        return GetContentArea(isCoverPage);
    }

    // (Cover/Continuation Headers and Footer omitted for brevity - kept below unchanged)
    
    /// <summary>
    /// Cover page: dark header + red title bar.
    /// </summary>
    private static void DrawCoverHeader(SKCanvas canvas, string title, string? logoPath)
    {
        // --- Dark header bar ---
        using var darkPaint = new SKPaint { Color = DarkBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, PageWidth, CoverHeaderHeight, darkPaint);

        // Logo
        var logo = GetLogo(logoPath);
        if (logo != null)
        {
            float logoMaxH = CoverHeaderHeight - 16f;
            float logoScale = logoMaxH / logo.Height;
            float logoW = logo.Width * logoScale;
            float logoH = logo.Height * logoScale;
            float logoX = Margin;
            float logoY = (CoverHeaderHeight - logoH) / 2f;
            canvas.DrawBitmap(logo, SKRect.Create(logoX, logoY, logoW, logoH));
        }

        // Date/time on the right
        using var datePaint = new SKPaint
        {
            Color = White,
            TextSize = 12,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal),
        };
        var dateStr = DateTime.Now.ToString("dd - MM - yy  HH:mm");
        var dateWidth = datePaint.MeasureText(dateStr);
        canvas.DrawText(dateStr, PageWidth - Margin - dateWidth, CoverHeaderHeight / 2f + 5f, datePaint);

        // --- Red title bar ---
        using var redPaint = new SKPaint { Color = RedBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, CoverHeaderHeight, PageWidth, CoverTitleBarHeight, redPaint);

        using var titlePaint = new SKPaint
        {
            Color = White,
            TextSize = 16,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };
        var titleBounds = new SKRect();
        titlePaint.MeasureText(title, ref titleBounds);
        float titleX = (PageWidth - titleBounds.Width) / 2f;
        float titleY = CoverHeaderHeight + (CoverTitleBarHeight + titleBounds.Height) / 2f;
        canvas.DrawText(title, titleX, titleY, titlePaint);
    }

    /// <summary>
    /// Continuation pages: slim dark header with "KTRN".
    /// </summary>
    private static void DrawContinuationHeader(SKCanvas canvas)
    {
        using var darkPaint = new SKPaint { Color = DarkBg, Style = SKPaintStyle.Fill };
        canvas.DrawRect(0, 0, PageWidth, ContinuationHeaderHeight, darkPaint);

        using var textPaint = new SKPaint
        {
            Color = White,
            TextSize = 13,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };
        canvas.DrawText("KTRN", Margin, ContinuationHeaderHeight / 2f + 5f, textPaint);
    }

    /// <summary>
    /// Draws the content area border.
    /// </summary>
    private static void DrawContentBorder(SKCanvas canvas, bool isCoverPage)
    {
        // Removed as per request to give more space
    }

    /// <summary>
    /// Draws centered footer with page number.
    /// </summary>
    private static void DrawFooter(SKCanvas canvas, int pageNumber)
    {
        using var footerPaint = new SKPaint
        {
            Color = Black,
            TextSize = 10,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
        };
        var text = $"PAGE {pageNumber}";
        var textWidth = footerPaint.MeasureText(text);
        float x = (PageWidth - textWidth) / 2f;
        float y = PageHeight - FooterHeight / 2f + 4f;
        canvas.DrawText(text, x, y, footerPaint);
    }

    /// <summary>
    /// Load and cache the logo from disk.
    /// </summary>
    private static SKBitmap? GetLogo(string? logoPath)
    {
        if (_logoBitmap != null) return _logoBitmap;

        lock (LogoLock)
        {
            if (_logoBitmap != null) return _logoBitmap;

            if (string.IsNullOrEmpty(logoPath) || !File.Exists(logoPath))
                return null;

            try
            {
                var fileInfo = new FileInfo(logoPath);
                if (fileInfo.Length < 100) return null; // placeholder/empty file guard

                _logoBitmap = SKBitmap.Decode(logoPath);
            }
            catch
            {
                // Silently fail – PDF still generates without logo
            }

            return _logoBitmap;
        }
    }

    /// <summary>
    /// Resets the cached logo (useful if you swap logo files at runtime).
    /// </summary>
    public static void ResetLogoCache()
    {
        lock (LogoLock)
        {
            _logoBitmap?.Dispose();
            _logoBitmap = null;
        }
    }
}
