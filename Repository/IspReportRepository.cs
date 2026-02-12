using System.Text;
using Dapper;
using isp_report_api.Data;
using isp_report_api.Models;

namespace isp_report_api.Repository;

public interface IIspReportRepository
{
    Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter);
    Task<IEnumerable<IspMonthlyReportSeries>> GetMonthlyReportsAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllIspNamesAsync();
    Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReport>> GetPostpaidReportsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReportSeries>> GetPostpaidReportsAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllPostpaidIspNamesAsync();
}

public class IspReportRepository : IIspReportRepository
{
    private readonly IOracleConnectionFactory _connectionFactory;

    public IspReportRepository(IOracleConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<IspMonthlyReport>> GetMonthlyReportsAsync(IspReportFilter filter)
    {
        var sql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(STATE_DATE, 'YYYYMM') AS UDay,
                COUNT(ACC_NBR) AS Purchase,
                SUM(SUBS_AMOUNT) / 100 AS Amount
            FROM RB_REPORT.REPORT_ALL_IPP
            WHERE 1=1"
        );

        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') <= TO_CHAR(ADD_MONTHS(SYSDATE, -1), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            sql.Append(" AND SP_NAME = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        sql.Append(
            @"
            GROUP BY TO_CHAR(STATE_DATE, 'YYYYMM')
            ORDER BY TO_CHAR(STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<IspMonthlyReport>(sql.ToString(), parameters);
    }

    public async Task<IEnumerable<IspMonthlyReportSeries>> GetMonthlyReportsAllIspsAsync(
        IspReportFilter filter
    )
    {
        var sql = new StringBuilder(
            @"
            SELECT 
                SP_NAME AS Isp,
                TO_CHAR(STATE_DATE, 'YYYYMM') AS UDay,
                COUNT(ACC_NBR) AS Purchase,
                SUM(SUBS_AMOUNT) / 100 AS Amount
            FROM RB_REPORT.REPORT_ALL_IPP
            WHERE SP_NAME IS NOT NULL"
        );

        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') <= TO_CHAR(ADD_MONTHS(SYSDATE, -1), 'YYYYMM')"
            );
        }

        sql.Append(
            @"
            GROUP BY SP_NAME, TO_CHAR(STATE_DATE, 'YYYYMM')
            ORDER BY SP_NAME ASC, TO_CHAR(STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var rows = await connection.QueryAsync<IspMonthlyReportSeriesRow>(
            sql.ToString(),
            parameters
        );

        return rows
            .GroupBy(r => r.Isp)
            .Select(g => new IspMonthlyReportSeries
            {
                Isp = g.Key,
                Points = g
                    .Select(r => new IspMonthlyReport
                    {
                        UDay = r.UDay,
                        Purchase = r.Purchase,
                        Amount = r.Amount,
                    })
                    .OrderBy(p => p.UDay)
                    .ToList(),
            });
    }

    public async Task<IEnumerable<string>> GetAllIspNamesAsync()
    {
        const string sql =
            @"
            SELECT DISTINCT SP_NAME
            FROM RB_REPORT.REPORT_ALL_IPP
            WHERE SP_NAME IS NOT NULL
            ORDER BY SP_NAME ASC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<string>(sql);
    }

    public async Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter)
    {
        var whereClause = new StringBuilder("WHERE 1=1");
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            whereClause.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            whereClause.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            whereClause.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            whereClause.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') <= TO_CHAR(ADD_MONTHS(SYSDATE, -1), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            whereClause.Append(" AND SP_NAME = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        var totalsSql =
            $@"
            SELECT 
                COUNT(ACC_NBR) AS TotalPurchases
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}";

        var ispStatsByAmountSql =
            $@"
            SELECT 
                SP_NAME AS Name,
                SUM(SUBS_AMOUNT) / 100 AS Amount,
                COUNT(ACC_NBR) AS Purchases
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}
            AND SP_NAME IS NOT NULL
            GROUP BY SP_NAME
            ORDER BY Amount DESC";

        var ispStatsByPurchasesSql =
            $@"
            SELECT 
                SP_NAME AS Name,
                SUM(SUBS_AMOUNT) / 100 AS Amount,
                COUNT(ACC_NBR) AS Purchases
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}
            AND SP_NAME IS NOT NULL
            GROUP BY SP_NAME
            ORDER BY Purchases DESC";

        var monthStatsSql =
            $@"
            SELECT 
                TO_CHAR(STATE_DATE, 'YYYYMM') AS Month,
                SUM(SUBS_AMOUNT) / 100 AS Amount,
                COUNT(ACC_NBR) AS Purchases
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}
            GROUP BY TO_CHAR(STATE_DATE, 'YYYYMM')
            ORDER BY Amount DESC";

        var monthlyPurchasesSql =
            $@"
            SELECT 
                TO_CHAR(STATE_DATE, 'YYYYMM') AS Month,
                COUNT(ACC_NBR) AS Purchases
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}
            GROUP BY TO_CHAR(STATE_DATE, 'YYYYMM')
            ORDER BY TO_CHAR(STATE_DATE, 'YYYYMM') DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var totalPurchases = await connection.QueryFirstOrDefaultAsync<int>(totalsSql, parameters);
        var ispStatsByAmount = (
            await connection.QueryAsync<IspStat>(ispStatsByAmountSql, parameters)
        ).ToList();
        var ispStatsByPurchases = (
            await connection.QueryAsync<IspStat>(ispStatsByPurchasesSql, parameters)
        ).ToList();
        var monthStats = (
            await connection.QueryAsync<MonthStat>(monthStatsSql, parameters)
        ).ToList();
        var monthlyPurchases = (
            await connection.QueryAsync<(string Month, int Purchases)>(
                monthlyPurchasesSql,
                parameters
            )
        ).ToList();

        var monthCount = monthStats.Count;
        var averagePurchases = monthCount > 0 ? (decimal)totalPurchases / monthCount : 0;

        decimal monthOverMonthGrowth = 0;
        if (monthlyPurchases.Count >= 2)
        {
            var lastMonth = monthlyPurchases[0].Purchases;
            var previousMonth = monthlyPurchases[1].Purchases;
            if (previousMonth > 0)
            {
                monthOverMonthGrowth = ((decimal)(lastMonth - previousMonth) / previousMonth) * 100;
            }
        }

        return new PrepaidStats
        {
            TotalPurchases = totalPurchases,
            AveragePurchases = Math.Round(averagePurchases, 2),
            MonthOverMonthGrowth = Math.Round(monthOverMonthGrowth, 2),
            TopIspByAmount = ispStatsByAmount.FirstOrDefault(),
            TopIspByPurchases = ispStatsByPurchases.FirstOrDefault(),
            LowestIsp = ispStatsByAmount.LastOrDefault(),
            HighestMonth = monthStats.FirstOrDefault(),
            LowestMonth = monthStats.LastOrDefault(),
        };
    }

    public async Task<IEnumerable<PostpaidReport>> GetPostpaidReportsAsync(IspReportFilter filter)
    {
        var sql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Period,
                b.sp_name AS Isp,
                ROUND(SUM(a.charge / 100), 0) AS EWallet
            FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
            WHERE a.acct_id = b.virt_acct_id
              AND b.sp_id > 1"
        );

        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(SYSDATE - 365, 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            sql.Append(" AND b.sp_name = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        sql.Append(
            @"
            GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM'), b.sp_name
            ORDER BY TO_CHAR(a.STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<PostpaidReport>(sql.ToString(), parameters);
    }

    public async Task<IEnumerable<PostpaidReportSeries>> GetPostpaidReportsAllIspsAsync(
        IspReportFilter filter
    )
    {
        var sql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Period,
                b.sp_name AS Isp,
                ROUND(SUM(a.charge / 100), 0) AS EWallet
            FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
            WHERE a.acct_id = b.virt_acct_id
              AND b.sp_id > 1
              AND b.sp_name IS NOT NULL"
        );

        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(SYSDATE - 365, 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }

        sql.Append(
            @"
            GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM'), b.sp_name
            ORDER BY b.sp_name ASC, TO_CHAR(a.STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var rows = await connection.QueryAsync<PostpaidReportSeriesRow>(
            sql.ToString(),
            parameters
        );

        return rows
            .GroupBy(r => r.Isp)
            .Select(g => new PostpaidReportSeries
            {
                Isp = g.Key,
                Points = g
                    .Select(r => new PostpaidReport
                    {
                        Period = r.Period,
                        Isp = r.Isp,
                        EWallet = r.EWallet,
                    })
                    .OrderBy(p => p.Period)
                    .ToList(),
            });
    }

    public async Task<IEnumerable<string>> GetAllPostpaidIspNamesAsync()
    {
        const string sql =
            @"
            SELECT DISTINCT b.sp_name
            FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
            WHERE a.acct_id = b.virt_acct_id
              AND b.sp_id > 1
              AND b.sp_name IS NOT NULL
            ORDER BY b.sp_name ASC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<string>(sql);
    }
}

file class IspMonthlyReportSeriesRow
{
    public string Isp { get; set; } = string.Empty;
    public string UDay { get; set; } = string.Empty;
    public int Purchase { get; set; }
    public decimal Amount { get; set; }
}

file class PostpaidReportSeriesRow
{
    public string Period { get; set; } = string.Empty;
    public string Isp { get; set; } = string.Empty;
    public decimal EWallet { get; set; }
}

