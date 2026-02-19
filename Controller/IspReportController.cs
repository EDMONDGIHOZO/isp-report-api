using isp_report_api.Models;
using isp_report_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace isp_report_api.Controller;

[ApiController]
[Route("api/[controller]")]
public class IspReportController : ControllerBase
{
    private readonly IIspReportService _ispReportService;
    private readonly IIspReportPdfService _pdfService;

    public IspReportController(
        IIspReportService ispReportService,
        IIspReportPdfService pdfService
    )
    {
        _ispReportService = ispReportService;
        _pdfService = pdfService;
    }

    [HttpGet("monthly")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyReports(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };

            // Parse day-level date range if provided
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fd))
                filter.FromDate = fd;
            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var td))
                filter.ToDate = td;

            var reports = await _ispReportService.GetMonthlyReportsAsync(filter);
            return Ok(reports);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = "Failed to fetch reports", Details = ex.Message });
        }
    }

        [HttpGet("monthly-all")]
        [Authorize]
        public async Task<IActionResult> GetMonthlyReportsAllIsps(
            [FromQuery] string? from,
            [FromQuery] string? to
        )
        {
            try
            {
                var filter = new IspReportFilter
                {
                    FromPeriod = from,
                    ToPeriod = to,
                    IspName = null,
                };

                var series = await _ispReportService.GetMonthlyReportsAllIspsAsync(filter);
                return Ok(series);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { Error = "Failed to fetch monthly reports for all ISPs", Details = ex.Message }
                );
            }
        }

    [HttpGet("monthly-all-pdf")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyReportsAllIspsPdf(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string chartType = "area"
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = null,
            };

            var pdfBytes = await _pdfService.GenerateAllIspsPdfAsync(filter, chartType);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"prepaid-sales-all-isps-{timestamp}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to generate PDF report", Details = ex.Message }
            );
        }
    }

    [HttpDelete("pdf-cache")]
    [Authorize]
    public IActionResult ClearPdfCache()
    {
        var deleted = _pdfService.ClearCache();
        return Ok(new
        {
            Message = $"Deleted {deleted} cached PDF(s)",
            CacheDirectory = _pdfService.GetCacheDirectory(),
        });
    }

    [HttpDelete("cache")]
    [Authorize]
    public async Task<IActionResult> ClearCache([FromQuery] string? type)
    {
        try
        {
            var cacheService = HttpContext.RequestServices.GetRequiredService<ICacheService>();
            
            if (!string.IsNullOrEmpty(type))
            {
                await cacheService.RemoveByTypeAsync(type);
                return Ok(new { Message = $"Cleared cache for type: {type}" });
            }
            
            // Clear all expired entries
            await cacheService.ClearExpiredAsync();
            return Ok(new { Message = "Cleared expired cache entries" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = "Failed to clear cache", Details = ex.Message });
        }
    }

    [HttpGet("isps")]
    [Authorize]
    public async Task<IActionResult> GetAllIspNames()
    {
        try
        {
            var isps = await _ispReportService.GetAllIspNamesAsync();
            return Ok(isps);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch ISP names", Details = ex.Message }
            );
        }
    }

    [HttpGet("traffic/daily")]
    [Authorize]
    public async Task<IActionResult> GetTrafficDaily(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };

            var reports = await _ispReportService.GetTrafficDailyAsync(filter);
            return Ok(reports);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch daily traffic reports", Details = ex.Message }
            );
        }
    }

    [HttpGet("traffic/daily-all")]
    [Authorize]
    public async Task<IActionResult> GetTrafficDailyAllIsps(
        [FromQuery] string? from,
        [FromQuery] string? to
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = null,
            };

            var series = await _ispReportService.GetTrafficDailyAllIspsAsync(filter);
            return Ok(series);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch daily traffic reports for all ISPs", Details = ex.Message }
            );
        }
    }

    [HttpGet("traffic/daily-all-pdf")]
    [Authorize]
    public async Task<IActionResult> GetTrafficDailyAllIspsPdf(
        [FromQuery] string? from,
        [FromQuery] string? to
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = null,
            };

            var pdfBytes = await _pdfService.GenerateTrafficAllIspsPdfAsync(filter);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"traffic-all-isps-{timestamp}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to generate traffic PDF report", Details = ex.Message }
            );
        }
    }

        [HttpGet("product-sales/weekly")]
        [Authorize]
        public async Task<IActionResult> GetProductWeeklySales()
        {
            try
            {
                var reports = await _ispReportService.GetProductWeeklySalesAsync();
                return Ok(reports);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { Error = "Failed to fetch product weekly sales", Details = ex.Message }
                );
            }
        }

        [HttpGet("product-sales/weekly-by-product")]
        [Authorize]
        public async Task<IActionResult> GetProductWeeklyBreakdown([FromQuery] string? isp)
        {
            if (string.IsNullOrWhiteSpace(isp))
            {
                return BadRequest(new { Error = "Query parameter 'isp' is required." });
            }

            try
            {
                var breakdown = await _ispReportService.GetProductWeeklyBreakdownAsync(isp);
                return Ok(breakdown);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { Error = "Failed to fetch product weekly breakdown", Details = ex.Message }
                );
            }
        }

        /// <summary>
        /// GET /api/IspReport/products
        /// Returns distinct products from CC.PROD_MAPPING.
        /// </summary>
        [HttpGet("products")]
        [Authorize]
        public async Task<IActionResult> GetProducts()
        {
            try
            {
                var products = await _ispReportService.GetProductsAsync();
                return Ok(products);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new { Error = "Failed to fetch products", Details = ex.Message }
                );
            }
        }

    [HttpGet("prepaid-stats")]
    [Authorize]
    public async Task<IActionResult> GetPrepaidStats(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };

            // Parse day-level date range if provided
            if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fd))
                filter.FromDate = fd;
            if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var td))
                filter.ToDate = td;

            var stats = await _ispReportService.GetPrepaidStatsAsync(filter);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch prepaid stats", Details = ex.Message }
            );
        }
    }

    [HttpGet("prepaid-retailers")]
    [Authorize]
    public async Task<IActionResult> GetPrepaidRetailerDistribution(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };

            var stats = await _ispReportService.GetPrepaidRetailerDistributionAsync(filter);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch prepaid retailer distribution", Details = ex.Message }
            );
        }
    }

    [HttpGet("postpaid/monthly")]
    [Authorize]
    public async Task<IActionResult> GetPostpaidReports(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };

            var reports = await _ispReportService.GetPostpaidReportsAsync(filter);
            return Ok(reports);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch postpaid reports", Details = ex.Message }
            );
        }
    }

    [HttpGet("postpaid/monthly-all")]
    [Authorize]
    public async Task<IActionResult> GetPostpaidReportsAllIsps(
        [FromQuery] string? from,
        [FromQuery] string? to
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = null,
            };

            var series = await _ispReportService.GetPostpaidReportsAllIspsAsync(filter);
            return Ok(series);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch postpaid reports for all ISPs", Details = ex.Message }
            );
        }
    }

    [HttpGet("postpaid/isps")]
    [Authorize]
    public async Task<IActionResult> GetAllPostpaidIspNames()
    {
        try
        {
            var isps = await _ispReportService.GetAllPostpaidIspNamesAsync();
            return Ok(isps);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch postpaid ISP names", Details = ex.Message }
            );
        }
    }

    [HttpGet("postpaid-stats")]
    [Authorize]
    public async Task<IActionResult> GetPostpaidStats(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? isp
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = isp,
            };
            var stats = await _ispReportService.GetPostpaidStatsAsync(filter);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch postpaid stats", Details = ex.Message }
            );
        }
    }

    [HttpGet("postpaid/monthly-all-pdf")]
    [Authorize]
    public async Task<IActionResult> GetPostpaidReportsAllIspsPdf(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string chartType = "line"
    )
    {
        try
        {
            var filter = new IspReportFilter
            {
                FromPeriod = from,
                ToPeriod = to,
                IspName = null,
            };

            var pdfBytes = await _pdfService.GeneratePostpaidAllIspsPdfAsync(filter, chartType);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"postpaid-sales-all-isps-{timestamp}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to generate postpaid PDF report", Details = ex.Message }
            );
        }
    }
}
