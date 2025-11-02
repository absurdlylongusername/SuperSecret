using Dapper;
using Microsoft.Extensions.Options;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Models;

namespace SuperSecret.Services;

public class SqlLinkStore : ILinkStore
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly int _maxTTLInMinutes;
    private DateTime MaxExpiryDate => DateTimeOffset.UtcNow.AddMinutes(_maxTTLInMinutes).UtcDateTime;

    public SqlLinkStore(IDbConnectionFactory connectionFactory, IOptions<TokenOptions> tokenOptions)
    {
        _connectionFactory = connectionFactory;
        _maxTTLInMinutes = tokenOptions.Value.MaxTTLInMinutes;
    }

    public async Task CreateAsync(SecretLinkClaims claims)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        var jtiBytes = claims.Jti.ToByteArray();
        var maxClicks = claims.Max ?? 1;
        var expiresAt = claims.Exp?.UtcDateTime ?? MaxExpiryDate;

        if (maxClicks == 1)
        {
            await conn.ExecuteAsync(
                DbObjects.Procs.CreateSingleUseLink,
                new { jti = jtiBytes, expiresAt },
                commandType: System.Data.CommandType.StoredProcedure);
        }
        else
        {
            await conn.ExecuteAsync(
                DbObjects.Procs.CreateMultiUseLink,
                new { jti = jtiBytes, clicksLeft = maxClicks, expiresAt },
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
