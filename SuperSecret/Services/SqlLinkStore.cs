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
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(false);

        var jtiBytes = claims.Jti.ToByteArray();
        var maxClicks = claims.Max ?? 1;

        if (maxClicks == 1)
        {
            await conn.ExecuteAsync(
                "dbo.CreateSingleUseLink",
                new { jti = jtiBytes, expiresAt = claims.Exp?.UtcDateTime },
                commandType: System.Data.CommandType.StoredProcedure);
        }
        else
        {
            await conn.ExecuteAsync(
                "dbo.CreateMultiUseLink",
                new { jti = jtiBytes, clicksLeft = maxClicks, expiresAt = claims.Exp?.UtcDateTime },
                commandType: System.Data.CommandType.StoredProcedure);
        }
    }

    public async Task<bool> ConsumeSingleUseAsync(Ulid jti, DateTimeOffset? expUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(false);

        var rowsAffected = await conn.QuerySingleOrDefaultAsync<int>(
            "dbo.ConsumeSingleUseLink",
            new { jti = jti.ToByteArray() },
            commandType: System.Data.CommandType.StoredProcedure);

        return rowsAffected > 0;
    }

    public async Task<int?> ConsumeMultiUseAsync(Ulid jti, DateTimeOffset? expUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync().ConfigureAwait(false);

        var result = await conn.QuerySingleOrDefaultAsync<int?>(
            "dbo.ConsumeMultiUseLink",
            new { jti = jti.ToByteArray() },
            commandType: System.Data.CommandType.StoredProcedure);

        return result;
    }
}
