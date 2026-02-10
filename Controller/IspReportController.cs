using isp_report_api.Models;
using isp_report_api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace isp_report_api.Controller;

[ApiController]
[Route("api/[controller]")]
public class IspReportController : ControllerBase
{
    private readonly IIspReportService _ispReportService;

    public IspReportController(IIspReportService ispReportService)
    {
        _ispReportService = ispReportService;
    }

    [HttpGet("monthly")]
    [Authorize]
    public async Task<IActionResult> GetMonthlyReports(
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

            var reports = await _ispReportService.GetMonthlyReportsAsync(filter);
            return Ok(reports);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { Error = "Failed to fetch reports", Details = ex.Message });
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

    [HttpGet("prepaid-stats")]
    [Authorize]
    public async Task<IActionResult> GetPrepaidStats(
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
}
