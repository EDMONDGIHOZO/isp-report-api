namespace isp_report_api.Models;

public class IspReportFilter
{
    public string? FromPeriod { get; set; }
    public string? ToPeriod { get; set; }
    public string? IspName { get; set; }
    
    /// <summary>
    /// Optional day-level date range start (inclusive). Format: yyyy-MM-dd.
    /// When set, FromPeriod/ToPeriod are ignored for the queries that support it.
    /// </summary>
    public DateTime? FromDate { get; set; }
    
    /// <summary>
    /// Optional day-level date range end (inclusive). Format: yyyy-MM-dd.
    /// Today's date should be excluded because its data is incomplete.
    /// </summary>
    public DateTime? ToDate { get; set; }
    
    /// <summary>True when FromDate/ToDate are provided, indicating day-level filtering.</summary>
    public bool HasDateRange => FromDate.HasValue && ToDate.HasValue;
}
