using Dapper;
using isp_report_api.Data;
using isp_report_api.Models;

namespace isp_report_api.Repository;

/// <summary>
/// Dapper-based implementation of <see cref="ISalesRepository"/>.
/// Executes the raw Oracle SQL to retrieve aggregated weekly sales data per ISP.
/// </summary>
public class SalesRepository : ISalesRepository
{
    private readonly IOracleConnectionFactory _connectionFactory;

    public SalesRepository(IOracleConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<WeeklySalesStat>> GetWeeklySalesAsync(
        string? ispName,
        string? from = null,
        string? to = null
    )
    {
        // Build dynamic date range bounds
        // Default: last 12 completed weeks (84 days)
        string dateFrom;
        string dateTo;
        string weekNumberBase;

        if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
        {
            // from/to are "YYYY-MM" — use first day of 'from' month and last day of 'to' month
            dateFrom = $"TO_DATE('{from}', 'YYYY-MM')";
            dateTo = $"LAST_DAY(TO_DATE('{to}', 'YYYY-MM'))";
            weekNumberBase = dateFrom;  // week numbering relative to start of range
        }
        else
        {
            dateFrom = "(TRUNC(SYSDATE - 4, 'IW') + 4 - 84)";
            dateTo = "(TRUNC(SYSDATE - 4, 'IW') + 4 - 7)";
            weekNumberBase = dateFrom;
        }

        string sql = $@"
            WITH WeekData AS (
              SELECT
                SP_NAME,
                TRUNC(STATE_DATE - 4, 'IW') + 4 AS week_start,
                TRUNC(STATE_DATE - 4, 'IW') + 4 + 6 AS week_end,
                ISP_AMOUNT,
                SUBS_AMOUNT,
                ORDER_NBR
              FROM RB_REPORT.REPORT_ALL_IPP
              WHERE TRUNC(STATE_DATE - 4, 'IW') + 4
                    BETWEEN {dateFrom}
                        AND {dateTo}
                AND SP_NAME <> 'KTRN'
            ),
            Aggregated AS (
              SELECT
                SP_NAME,
                week_start,
                week_end,
                ROUND(SUM(ISP_AMOUNT/100), 0) AS Wholesale_RWF,
                SUM(SUBS_AMOUNT/100) AS Retail_RWF,
                COUNT(ORDER_NBR) AS Purchases
              FROM WeekData
              GROUP BY SP_NAME, week_start, week_end
            )
            SELECT
              SP_NAME AS Isp,
              'W' || TO_CHAR(TRUNC((week_start - {weekNumberBase}) / 7) + 1)
                 || ' (' || TO_CHAR(week_start, 'DD/MM/YY') || ' to ' ||
                            TO_CHAR(week_end, 'DD/MM/YY') || ')' AS Period,
              Wholesale_RWF AS WholesaleRwf,
              Retail_RWF AS RetailRwf,
              Purchases,
              CASE 
                WHEN Retail_RWF <> 0 THEN ROUND(((Retail_RWF - Wholesale_RWF) / Retail_RWF) * 100, 1)
                ELSE 0
              END AS MarginPercent
            FROM Aggregated
            ORDER BY SP_NAME, week_start";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var results = await connection.QueryAsync<WeeklySalesStat>(sql);

        // Optional client-side ISP filter
        if (!string.IsNullOrWhiteSpace(ispName))
        {
            results = results.Where(r =>
                r.Isp.Equals(ispName, StringComparison.OrdinalIgnoreCase));
        }

        return results;
    }
}
