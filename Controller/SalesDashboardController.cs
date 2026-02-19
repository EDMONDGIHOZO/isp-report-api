using isp_report_api.Repository;
using isp_report_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace isp_report_api.Controller;

[ApiController]
[Route("api/sales")]
public class SalesDashboardController : ControllerBase
{
    private readonly ISalesRepository _salesRepository;
    private readonly IIspReportPdfService _pdfService;
    private readonly IProductSalesReportService _productSalesReportService;

    public SalesDashboardController(
        ISalesRepository salesRepository,
        IIspReportPdfService pdfService,
        IProductSalesReportService productSalesReportService
    )
    {
        _salesRepository = salesRepository;
        _pdfService = pdfService;
        _productSalesReportService = productSalesReportService;
    }

    /// <summary>
    /// GET /api/sales/products?startDate=&amp;endDate=&amp;isp=&amp;product=&amp;category=&amp;topN=&amp;groupBy=&amp;excludeIsps=
    /// Returns product-level sales report with KPIs, weekly trend, ISP comparison, top products, heatmap, pivot table, and raw data.
    /// </summary>
    [HttpGet("products")]
    [Authorize]
    public async Task<IActionResult> GetProductSales(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] string? isp,
        [FromQuery] string? product,
        [FromQuery] string? category,
        [FromQuery] int? topN,
        [FromQuery] string? groupBy,
        [FromQuery] string? excludeIsps
    )
    {
        try
        {
            var ispList = ParseListParam(isp);
            var productList = ParseListParam(product);
            var categoryList = ParseListParam(category);
            var excludeList = ParseListParam(excludeIsps);
            var response = await _productSalesReportService.GetProductSalesReportAsync(
                startDate,
                endDate,
                ispList.Count > 0 ? ispList : null,
                productList.Count > 0 ? productList : null,
                categoryList.Count > 0 ? categoryList : null,
                excludeList.Count > 0 ? excludeList : null,
                topN ?? 10,
                groupBy
            );
            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch product sales report", Details = ex.Message }
            );
        }
    }

    private static List<string> ParseListParam(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>
    /// GET /api/sales/weekly?isp={ispName}&amp;from={YYYY-MM}&amp;to={YYYY-MM}
    /// Returns aggregated weekly sales data. Optionally filter by ISP and date range.
    /// </summary>
    [HttpGet("weekly")]
    [Authorize]
    public async Task<IActionResult> GetWeeklySales(
        [FromQuery] string? isp,
        [FromQuery] string? from,
        [FromQuery] string? to
    )
    {
        try
        {
            var data = await _salesRepository.GetWeeklySalesAsync(isp, from, to);
            return Ok(data);
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new { Error = "Failed to fetch weekly sales data", Details = ex.Message }
            );
        }
    }

    [HttpGet("weekly/pdf")]
    [Authorize]
    public async Task<IActionResult> GetWeeklyMatrixPdf(
        [FromQuery] string? isp,
        [FromQuery] string? weeks
    )
    {
        try
        {
            var pdfBytes = await _pdfService.GenerateWeeklyMatrixPdfAsync(isp, weeks);
            var fileName = string.IsNullOrWhiteSpace(isp)
                ? "weekly-isp-matrix.pdf"
                : $"weekly-isp-matrix-{isp}.pdf";

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
                new
                {
                    Error = "Failed to generate weekly matrix PDF",
                    Details = ex.Message,
                }
            );
        }
    }

    /// <summary>
    /// GET /api/sales/products/pivot-pdf?isp={ispName}
    /// Returns a PDF of the product × week pivot table for the selected ISP.
    /// </summary>
    [HttpGet("products/pivot-pdf")]
    [Authorize]
    public async Task<IActionResult> GetProductPivotPdf([FromQuery] string? isp)
    {
        if (string.IsNullOrWhiteSpace(isp))
            return BadRequest(new { Error = "ISP name is required (query parameter: isp)." });

        try
        {
            var pdfBytes = await _pdfService.GenerateProductPivotPdfAsync(isp);
            var fileName = $"pivot-table-report-{isp}.pdf";
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
                new { Error = "Failed to generate product pivot PDF", Details = ex.Message }
            );
        }
    }
}
