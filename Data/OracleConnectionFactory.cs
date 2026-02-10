using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace isp_report_api.Data;

public interface IOracleConnectionFactory
{
    IDbConnection CreateConnection();
}

public class OracleConnectionFactory : IOracleConnectionFactory
{
    private readonly string _connectionString;

    public OracleConnectionFactory(IConfiguration configuration)
    {
        _connectionString =
            configuration.GetConnectionString("OracleDB")
            ?? throw new InvalidOperationException("Connection string 'OracleDB' not found.");
    }

    public IDbConnection CreateConnection()
    {
        return new OracleConnection(_connectionString);
    }
}
