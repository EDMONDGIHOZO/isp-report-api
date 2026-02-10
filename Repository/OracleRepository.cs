using System.Data;
using Dapper;
using isp_report_api.Data;

namespace isp_report_api.Repository;

public class OracleRepository : IOracleRepository
{
    private readonly IOracleConnectionFactory _connectionFactory;

    public OracleRepository(IOracleConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryAsync<T>(sql, parameters);
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? parameters = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QueryFirstOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<T> QuerySingleAsync<T>(string sql, object? parameters = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.QuerySingleAsync<T>(sql, parameters);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.ExecuteAsync(sql, parameters);
    }
}
