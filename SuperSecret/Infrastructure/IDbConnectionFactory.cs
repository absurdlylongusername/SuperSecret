using Microsoft.Data.SqlClient;

namespace SuperSecret.Infrastructure;

public interface IDbConnectionFactory
{
    Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}