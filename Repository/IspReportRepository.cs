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
    Task<IEnumerable<IspStat>> GetPrepaidRetailerDistributionAsync(IspReportFilter filter);
    Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReport>> GetPostpaidReportsAsync(IspReportFilter filter);
    Task<IEnumerable<PostpaidReportSeries>> GetPostpaidReportsAllIspsAsync(IspReportFilter filter);
    Task<IEnumerable<string>> GetAllPostpaidIspNamesAsync();
    Task<PostpaidStats> GetPostpaidStatsAsync(IspReportFilter filter);
    Task<IEnumerable<TrafficReport>> GetTrafficDailyAsync(IspReportFilter filter);
    Task<IEnumerable<TrafficReportSeries>> GetTrafficDailyAllIspsAsync(IspReportFilter filter);
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
        // Day-level date range filtering — aggregates by day, always excludes today
        if (filter.HasDateRange)
        {
            // Clamp ToDate to yesterday to always exclude today's incomplete data
            var today = DateTime.Now.Date;
            var effectiveTo = filter.ToDate!.Value.Date >= today
                ? today.AddDays(-1)
                : filter.ToDate!.Value.Date;

            var sql = new StringBuilder(
                @"
                SELECT 
                    TO_CHAR(STATE_DATE, 'YYYY-MM-DD') AS UDay,
                    COUNT(ACC_NBR) AS Purchase,
                    SUM(SUBS_AMOUNT) / 100 AS Amount
                FROM RB_REPORT.REPORT_ALL_IPP
                WHERE TRUNC(STATE_DATE) >= :FromDate
                  AND TRUNC(STATE_DATE) <= :ToDate"
            );

            var parameters = new DynamicParameters();
            parameters.Add("FromDate", filter.FromDate!.Value.Date);
            parameters.Add("ToDate", effectiveTo);

            if (!string.IsNullOrEmpty(filter.IspName))
            {
                sql.Append(" AND SP_NAME = :IspName");
                parameters.Add("IspName", filter.IspName);
            }

            sql.Append(
                @"
                GROUP BY TO_CHAR(STATE_DATE, 'YYYY-MM-DD')
                ORDER BY TO_CHAR(STATE_DATE, 'YYYY-MM-DD') ASC"
            );

            using var dateRangeConnection = _connectionFactory.CreateConnection();
            dateRangeConnection.Open();
            return await dateRangeConnection.QueryAsync<IspMonthlyReport>(sql.ToString(), parameters);
        }

        // Normal monthly view (excludes current month)
        var monthlySql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(STATE_DATE, 'YYYYMM') AS UDay,
                COUNT(ACC_NBR) AS Purchase,
                SUM(SUBS_AMOUNT) / 100 AS Amount
            FROM RB_REPORT.REPORT_ALL_IPP
            WHERE 1=1"
        );

        var monthlyParameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            monthlySql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
            monthlyParameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            // 13 completed months back (exclude current incomplete month)
            monthlySql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            monthlySql.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') <= :ToPeriod");
            monthlyParameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            // Exclude current month (incomplete); show up to last completed month
            monthlySql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            monthlySql.Append(" AND SP_NAME = :IspName");
            monthlyParameters.Add("IspName", filter.IspName);
        }

        monthlySql.Append(
            @"
            GROUP BY TO_CHAR(STATE_DATE, 'YYYYMM')
            ORDER BY TO_CHAR(STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<IspMonthlyReport>(monthlySql.ToString(), monthlyParameters);
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
            // 13 completed months back (exclude current incomplete month)
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
            // Exclude current month (incomplete); show up to last completed month
            sql.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
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

    public async Task<IEnumerable<IspStat>> GetPrepaidRetailerDistributionAsync(
        IspReportFilter filter
    )
    {
        var whereClause = new StringBuilder("WHERE SP_NAME IS NOT NULL");
        var parameters = new DynamicParameters();

        // We ignore weekly toggle here and always work on monthly aggregates
        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            whereClause.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            // 13 completed months back (exclude current incomplete month)
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
            // Exclude current month (incomplete); show up to last completed month
            whereClause.Append(
                " AND TO_CHAR(STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            whereClause.Append(" AND SP_NAME = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        var sql =
            $@"
            SELECT 
                SP_NAME AS Name,
                COUNT(ACC_NBR) AS Purchases,
                SUM(SUBS_AMOUNT) / 100 AS Amount
            FROM RB_REPORT.REPORT_ALL_IPP
            {whereClause}
            GROUP BY SP_NAME
            ORDER BY SUM(SUBS_AMOUNT) DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<IspStat>(sql, parameters);
    }

    public async Task<PrepaidStats> GetPrepaidStatsAsync(IspReportFilter filter)
    {
        var whereClause = new StringBuilder("WHERE 1=1");
        var parameters = new DynamicParameters();

        // Day-level date range filtering — always exclude today
        if (filter.HasDateRange)
        {
            var today = DateTime.Now.Date;
            var effectiveTo = filter.ToDate!.Value.Date >= today
                ? today.AddDays(-1)
                : filter.ToDate!.Value.Date;

            whereClause.Append(" AND TRUNC(STATE_DATE) >= :FromDate AND TRUNC(STATE_DATE) <= :ToDate");
            parameters.Add("FromDate", filter.FromDate!.Value.Date);
            parameters.Add("ToDate", effectiveTo);
        }
        else
        {
            if (!string.IsNullOrEmpty(filter.FromPeriod))
            {
                whereClause.Append(" AND TO_CHAR(STATE_DATE, 'YYYYMM') >= :FromPeriod");
                parameters.Add("FromPeriod", filter.FromPeriod);
            }
            else
            {
                // 13 completed months back (exclude current incomplete month)
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
                // Exclude current month (incomplete); show up to last completed month
                whereClause.Append(
                    " AND TO_CHAR(STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
                );
            }
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
        var parameters = new DynamicParameters();

        // When no ISP filter: return one row per period with EWallet summed across all ISPs
        if (string.IsNullOrEmpty(filter.IspName))
        {
            var sqlAll = new StringBuilder(
                @"
                SELECT 
                    TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Period,
                    'All ISPs' AS Isp,
                    ROUND(SUM(a.charge / 100), 0) AS EWallet
                FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
                WHERE a.acct_id = b.virt_acct_id
                  AND b.sp_id > 1"
            );

            if (!string.IsNullOrEmpty(filter.FromPeriod))
            {
                sqlAll.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= :FromPeriod");
                parameters.Add("FromPeriod", filter.FromPeriod);
            }
            else
            {
                // 13 completed months back (exclude current incomplete month)
                sqlAll.Append(
                    " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
                );
            }

            if (!string.IsNullOrEmpty(filter.ToPeriod))
            {
                sqlAll.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
                parameters.Add("ToPeriod", filter.ToPeriod);
            }
            else
            {
                // Exclude current month (incomplete); show up to last completed month
                sqlAll.Append(
                    " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
                );
            }

            sqlAll.Append(
                @"
                GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM')
                ORDER BY TO_CHAR(a.STATE_DATE, 'YYYYMM') ASC"
            );

            using var connection = _connectionFactory.CreateConnection();
            connection.Open();
            return await connection.QueryAsync<PostpaidReport>(sqlAll.ToString(), parameters);
        }

        // When ISP filter is set: return one row per period for that ISP only
        var sql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Period,
                b.sp_name AS Isp,
                ROUND(SUM(a.charge / 100), 0) AS EWallet
            FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
            WHERE a.acct_id = b.virt_acct_id
              AND b.sp_id > 1
              AND b.sp_name = :IspName"
        );

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            // 13 completed months back (exclude current incomplete month)
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            // Exclude current month (incomplete); show up to last completed month
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
            );
        }

        parameters.Add("IspName", filter.IspName);

        sql.Append(
            @"
            GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM'), b.sp_name
            ORDER BY TO_CHAR(a.STATE_DATE, 'YYYYMM') ASC"
        );

        using var connection2 = _connectionFactory.CreateConnection();
        connection2.Open();
        return await connection2.QueryAsync<PostpaidReport>(sql.ToString(), parameters);
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
            // 13 completed months back (exclude current incomplete month)
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            // Exclude current month (incomplete); show up to last completed month
            sql.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
            );
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

    public async Task<PostpaidStats> GetPostpaidStatsAsync(IspReportFilter filter)
    {
        var whereClause = new StringBuilder(
            @"FROM acct_item@pub_link_cc a, part_sp@pub_link_cc b
            WHERE a.acct_id = b.virt_acct_id
              AND b.sp_id > 1"
        );
        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            whereClause.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            // 13 completed months back (exclude current incomplete month)
            whereClause.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') >= TO_CHAR(ADD_MONTHS(SYSDATE, -13), 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            whereClause.Append(" AND TO_CHAR(a.STATE_DATE, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            // Exclude current month (incomplete); show up to last completed month
            whereClause.Append(
                " AND TO_CHAR(a.STATE_DATE, 'YYYYMM') < TO_CHAR(SYSDATE, 'YYYYMM')"
            );
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            whereClause.Append(" AND b.sp_name = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        var totalsSql =
            $@"
            SELECT 
                ROUND(SUM(a.charge / 100), 0) AS TotalEWallet
            {whereClause}";

        var ispStatsByEWalletSql =
            $@"
            SELECT 
                b.sp_name AS Name,
                ROUND(SUM(a.charge / 100), 0) AS Amount,
                0 AS Purchases
            {whereClause}
            AND b.sp_name IS NOT NULL
            GROUP BY b.sp_name
            ORDER BY Amount DESC";

        var monthStatsSql =
            $@"
            SELECT 
                TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Month,
                ROUND(SUM(a.charge / 100), 0) AS Amount,
                0 AS Purchases
            {whereClause}
            GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM')
            ORDER BY Amount DESC";

        var monthlyEWalletSql =
            $@"
            SELECT 
                TO_CHAR(a.STATE_DATE, 'YYYYMM') AS Month,
                ROUND(SUM(a.charge / 100), 0) AS EWallet
            {whereClause}
            GROUP BY TO_CHAR(a.STATE_DATE, 'YYYYMM')
            ORDER BY TO_CHAR(a.STATE_DATE, 'YYYYMM') DESC";

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        var totalEWallet = await connection.QueryFirstOrDefaultAsync<decimal>(totalsSql, parameters);
        var ispStatsByEWallet = (
            await connection.QueryAsync<IspStat>(ispStatsByEWalletSql, parameters)
        ).ToList();
        var monthStats = (
            await connection.QueryAsync<MonthStat>(monthStatsSql, parameters)
        ).ToList();
        var monthlyEWallet = (
            await connection.QueryAsync<(string Month, decimal EWallet)>(
                monthlyEWalletSql,
                parameters
            )
        ).ToList();

        var monthCount = monthStats.Count;
        var averageEWallet = monthCount > 0 ? totalEWallet / monthCount : 0;

        decimal monthOverMonthGrowth = 0;
        if (monthlyEWallet.Count >= 2)
        {
            var lastMonth = monthlyEWallet[0].EWallet;
            var previousMonth = monthlyEWallet[1].EWallet;
            if (previousMonth > 0)
            {
                monthOverMonthGrowth = ((lastMonth - previousMonth) / previousMonth) * 100;
            }
        }

        return new PostpaidStats
        {
            TotalEWallet = totalEWallet,
            AverageEWallet = Math.Round(averageEWallet, 2),
            MonthOverMonthGrowth = Math.Round(monthOverMonthGrowth, 2),
            TopIspByEWallet = ispStatsByEWallet.FirstOrDefault(),
            LowestIsp = ispStatsByEWallet.LastOrDefault(),
            HighestMonth = monthStats.FirstOrDefault(),
            LowestMonth = monthStats.LastOrDefault(),
        };
    }

    public async Task<IEnumerable<TrafficReport>> GetTrafficDailyAsync(IspReportFilter filter)
    {
        var sql = new StringBuilder(
            @"
            SELECT 
                TO_CHAR(TRUNC(STATE_DAY), 'YYYYMMDD') AS UDay,
                SP_NAME AS Isp,
                COUNT(DISTINCT ACC_NBR) AS Subs,
                ROUND(SUM(USG_AMOUNT_KB) / 1024 / 1024, 0) AS UsgGb
            FROM RB_REPORT.REPORT_DAILY_USAGE
            WHERE PROD_CODE != 'ISP Default PP'"
        );

        var parameters = new DynamicParameters();

        if (!string.IsNullOrEmpty(filter.FromPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DAY, 'YYYYMM') >= :FromPeriod");
            parameters.Add("FromPeriod", filter.FromPeriod);
        }
        else
        {
            sql.Append(" AND STATE_DAY >= TRUNC(SYSDATE - 31)");
        }

        if (!string.IsNullOrEmpty(filter.ToPeriod))
        {
            sql.Append(" AND TO_CHAR(STATE_DAY, 'YYYYMM') <= :ToPeriod");
            parameters.Add("ToPeriod", filter.ToPeriod);
        }
        else
        {
            sql.Append(" AND STATE_DAY < TRUNC(SYSDATE)");
        }

        if (!string.IsNullOrEmpty(filter.IspName))
        {
            sql.Append(" AND SP_NAME = :IspName");
            parameters.Add("IspName", filter.IspName);
        }

        sql.Append(
            @"
            GROUP BY TRUNC(STATE_DAY), SP_NAME
            ORDER BY TRUNC(STATE_DAY) ASC, SP_NAME ASC"
        );

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<TrafficReport>(sql.ToString(), parameters);
    }

    public async Task<IEnumerable<TrafficReportSeries>> GetTrafficDailyAllIspsAsync(
        IspReportFilter filter
    )
    {
        var rows = await GetTrafficDailyAsync(filter);

        return rows
            .GroupBy(r => r.Isp)
            .Select(g => new TrafficReportSeries
            {
                Isp = g.Key,
                Points = g
                    .OrderBy(p => p.UDay)
                    .ToList(),
            });
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

