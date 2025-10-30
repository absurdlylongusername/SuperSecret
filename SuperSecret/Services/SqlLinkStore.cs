using Dapper;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Models;

namespace SuperSecret.Services;

public class SqlLinkStore : ILinkStore
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqlLinkStore(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task CreateAsync(SecretLinkClaims claims)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        var jtiBytes = claims.Jti.ToByteArray();
        var maxClicks = claims.Max ?? 1;

        if (maxClicks == 1)
        {
            await conn.ExecuteAsync(
                DbObjects.Procs.CreateSingleUseLink,
                new { jti = jtiBytes, expiresAt = claims.Exp?.UtcDateTime },
                commandType: System.Data.CommandType.StoredProcedure);
        }
        else
        {
            await conn.ExecuteAsync(
                DbObjects.Procs.CreateMultiUseLink,
                new { jti = jtiBytes, clicksLeft = maxClicks, expiresAt = claims.Exp?.UtcDateTime },
                commandType: System.Data.CommandType.StoredProcedure);
        }
    }

    public async Task<bool> ConsumeSingleUseAsync(Ulid jti, DateTimeOffset? expUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        var rowsAffected =
            await conn.QuerySingleAsync<int>(
                DbObjects.Procs.ConsumeSingleUseLink,
                new { jti = jti.ToByteArray() },
                commandType: System.Data.CommandType.StoredProcedure);

        return rowsAffected > 0;
    }

    public async Task<int?> ConsumeMultiUseAsync(Ulid jti, DateTimeOffset? expUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        var result = await conn.QuerySingleOrDefaultAsync<int?>(
            DbObjects.Procs.ConsumeMultiUseLink,
            new { jti = jti.ToByteArray() },
            commandType: System.Data.CommandType.StoredProcedure);

        return result;
    }
}
