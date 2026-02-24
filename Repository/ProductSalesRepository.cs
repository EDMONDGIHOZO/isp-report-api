using Dapper;
using isp_report_api.Data;
using isp_report_api.Models;

namespace isp_report_api.Repository;

public interface IProductSalesRepository
{
    Task<IEnumerable<Product>> GetProductsAsync();
    Task<IEnumerable<ProductWeeklySales>> GetProductWeeklySalesAsync();
    Task<IEnumerable<ProductWeeklyBreakdown>> GetProductWeeklyBreakdownAsync(string ispName);
    Task<IEnumerable<ProductSalesRawRow>> GetProductSalesRawAsync(
        DateTime? startDate,
        DateTime? endDate,
        IReadOnlyList<string> excludeIsps,
        IReadOnlyList<string>? ispFilter = null,
        IReadOnlyList<string>? productFilter = null,
        IReadOnlyList<string>? categoryFilter = null
    );
}

public class ProductSalesRepository : IProductSalesRepository
{
    private readonly IOracleConnectionFactory _connectionFactory;

    public ProductSalesRepository(IOracleConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        const string sql =
            @"
            SELECT DISTINCT
                IPP_OFFER_CODE AS IppOfferCode,
                IPP_OFFER_CODE AS ProductName
            FROM RB_REPORT.REPORT_ALL_IPP
            WHERE SP_NAME <> 'KTRN'
            ORDER BY IPP_OFFER_CODE";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<Product>(sql);
    }

    public async Task<IEnumerable<ProductWeeklySales>> GetProductWeeklySalesAsync()
    {
        const string sql =
            @"
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
                    BETWEEN (TRUNC(SYSDATE - 4, 'IW') + 4 - 84)
                        AND (TRUNC(SYSDATE - 4, 'IW') + 4 - 7)
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
                'W' || TO_CHAR(TRUNC((week_start - ((TRUNC(SYSDATE - 4, 'IW') + 4) - 84)) / 7) + 1)
                    || ' (' || TO_CHAR(week_start, 'DD/MM') || ' to ' ||
                               TO_CHAR(week_end, 'DD/MM') || ')' AS Period,
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
        return await connection.QueryAsync<ProductWeeklySales>(sql);
    }

    public async Task<IEnumerable<ProductWeeklyBreakdown>> GetProductWeeklyBreakdownAsync(
        string ispName
    )
    {
        const string sql =
            @"
            WITH WeekData AS (
                SELECT
                    RA.SP_NAME,
                    PM.F_PROD_NAME,
                    TRUNC(RA.STATE_DATE - 4, 'IW') + 4 AS week_start,
                    TRUNC(RA.STATE_DATE - 4, 'IW') + 4 + 6 AS week_end,
                    RA.ORDER_NBR
                FROM RB_REPORT.REPORT_ALL_IPP RA
                JOIN RB.PROD_MAPPING PM
                    ON RA.IPP_OFFER_CODE = PM.IPP_OFFER_CODE
                WHERE TRUNC(RA.STATE_DATE - 4, 'IW') + 4
                    BETWEEN (TRUNC(SYSDATE - 4, 'IW') + 4 - 84)
                        AND (TRUNC(SYSDATE - 4, 'IW') + 4 - 7)
                    AND RA.SP_NAME <> 'KTRN'
                    AND RA.SP_NAME = :IspName
            ),
            Aggregated AS (
                SELECT
                    F_PROD_NAME,
                    week_start,
                    week_end,
                    COUNT(ORDER_NBR) AS Purchases
                FROM WeekData
                GROUP BY F_PROD_NAME, week_start, week_end
            )
            SELECT
                F_PROD_NAME AS ProductName,
                'W' || TO_CHAR(TRUNC((week_start - ((TRUNC(SYSDATE - 4, 'IW') + 4) - 84)) / 7) + 1)
                    || ' (' || TO_CHAR(week_start, 'DD/MM/YY') || ' to ' ||
                               TO_CHAR(week_end, 'DD/MM/YY') || ')' AS Period,
                Purchases
            FROM Aggregated
            ORDER BY F_PROD_NAME, week_start";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<ProductWeeklyBreakdown>(
            sql,
            new { IspName = ispName }
        );
    }

    public async Task<IEnumerable<ProductSalesRawRow>> GetProductSalesRawAsync(
        DateTime? startDate,
        DateTime? endDate,
        IReadOnlyList<string> excludeIsps,
        IReadOnlyList<string>? ispFilter = null,
        IReadOnlyList<string>? productFilter = null,
        IReadOnlyList<string>? categoryFilter = null
    )
    {
        var exclude = excludeIsps.Count > 0
            ? excludeIsps.ToList()
            : new List<string> { "KTRN" };
        if (!exclude.Exists(s => string.Equals(s, "KTRN", StringComparison.OrdinalIgnoreCase)))
            exclude.Add("KTRN");

        string dateFrom, dateTo;
        if (startDate.HasValue && endDate.HasValue)
        {
            dateFrom = ":StartDate";
            dateTo = ":EndDate";
        }
        else
        {
            dateFrom = "(TRUNC(SYSDATE - 4, 'IW') + 4 - 84)";
            dateTo = "(TRUNC(SYSDATE - 4, 'IW') + 4 - 7)";
        }

        var excludePlaceholders = string.Join(", ", exclude.Select((_, i) => $":exclude{i}"));
        var sql =
            $@"
            WITH WeekData AS (
                SELECT
                    RA.SP_NAME,
                    PM.F_PROD_NAME,
                    TRUNC(RA.STATE_DATE - 4, 'IW') + 4 AS week_start,
                    TRUNC(RA.STATE_DATE - 4, 'IW') + 4 + 6 AS week_end,
                    RA.ORDER_NBR,
                    RA.SUBS_AMOUNT,
                    CASE
                        WHEN UPPER(PM.F_PROD_NAME) LIKE '%MONTHLY%' THEN 'Monthly'
                        WHEN UPPER(PM.F_PROD_NAME) LIKE '%BUNDLE%' THEN 'Bundle'
                        WHEN UPPER(PM.F_PROD_NAME) LIKE '%DAILY%' THEN 'Daily'
                        ELSE 'Other'
                    END AS CATEGORY
                FROM RB_REPORT.REPORT_ALL_IPP RA
                JOIN RB.PROD_MAPPING PM ON RA.IPP_OFFER_CODE = PM.IPP_OFFER_CODE
                WHERE TRUNC(RA.STATE_DATE - 4, 'IW') + 4 BETWEEN {dateFrom} AND {dateTo}
                    AND RA.SP_NAME NOT IN ({excludePlaceholders})
            ),
            Aggregated AS (
                SELECT
                    SP_NAME,
                    F_PROD_NAME,
                    week_start,
                    week_end,
                    MAX(CATEGORY) AS CATEGORY,
                    COUNT(ORDER_NBR) AS Purchases,
                    SUM(SUBS_AMOUNT) / 100 AS RetailRwf
                FROM WeekData
                GROUP BY SP_NAME, F_PROD_NAME, week_start, week_end
            ),
            AggregatedWithIndex AS (
                SELECT
                    SP_NAME,
                    F_PROD_NAME,
                    week_start,
                    week_end,
                    CATEGORY,
                    Purchases,
                    RetailRwf,
                    DENSE_RANK() OVER (ORDER BY week_start) AS WeekIndex
                FROM Aggregated
            )
            SELECT
                SP_NAME AS Isp,
                F_PROD_NAME AS FProdName,
                'W' || TO_CHAR(WeekIndex)
                    || ' (' || TO_CHAR(week_start, 'DD/MM/YY') || ' to ' || TO_CHAR(week_end, 'DD/MM/YY') || ')' AS Period,
                CATEGORY AS Category,
                Purchases,
                RetailRwf
            FROM AggregatedWithIndex
            ORDER BY SP_NAME, F_PROD_NAME, week_start";

        var dyn = new DynamicParameters();
        for (var i = 0; i < exclude.Count; i++)
            dyn.Add($"exclude{i}", exclude[i]);
        if (startDate.HasValue)
            dyn.Add("StartDate", startDate.Value.Date, System.Data.DbType.DateTime);
        if (endDate.HasValue)
            dyn.Add("EndDate", endDate.Value.Date, System.Data.DbType.DateTime);

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var rows = (await connection.QueryAsync<ProductSalesRawRow>(sql, dyn)).ToList();

        if (ispFilter is { Count: > 0 })
        {
            var ispSet = new HashSet<string>(ispFilter, StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(r => ispSet.Contains(r.Isp)).ToList();
        }
        if (productFilter is { Count: > 0 })
        {
            var prodSet = new HashSet<string>(productFilter, StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(r => prodSet.Contains(r.FProdName)).ToList();
        }
        if (categoryFilter is { Count: > 0 } && categoryFilter.All(c => !string.IsNullOrEmpty(c)))
        {
            var catSet = new HashSet<string>(categoryFilter.Where(c => !string.IsNullOrEmpty(c)), StringComparer.OrdinalIgnoreCase);
            rows = rows.Where(r => r.Category != null && catSet.Contains(r.Category)).ToList();
        }

        return rows;
    }
}

