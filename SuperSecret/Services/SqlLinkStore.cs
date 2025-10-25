using Dapper;
using Microsoft.Data.SqlClient;
using SuperSecret.Models;

namespace SuperSecret.Services;

public class SqlLinkStore : ILinkStore
{
    private readonly string _connectionString;

    public SqlLinkStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured");
    }

    public async Task CreateAsync(SecretLinkClaims claims)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var maxClicks = claims.Max ?? 1;
        if (maxClicks == 1)
        {
            // Single-use link
            await conn.ExecuteAsync(
                "INSERT INTO dbo.SingleUseLinks (Jti, ExpiresAt) VALUES (@jti, @expiresAt)",
                new { jti = claims.Jti, expiresAt = claims.Exp?.UtcDateTime });
        }
        else
        {
            // Multi-use link
            await conn.ExecuteAsync(
                "INSERT INTO dbo.MultiUseLinks (Jti, ClicksLeft, ExpiresAt) VALUES (@jti, @clicksLeft, @expiresAt)",
                new { jti = claims.Jti, clicksLeft = maxClicks, expiresAt = claims.Exp?.UtcDateTime });
        }
    }

    public async Task<bool> ConsumeSingleUseAsync(string jti, DateTimeOffset? expUtc)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var rowsAffected = await conn.ExecuteAsync(
            @"DELETE FROM dbo.SingleUseLinks
              WHERE Jti=@jti AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME())",
            new { jti });

        return rowsAffected > 0;
    }

    public async Task<int?> ConsumeMultiUseAsync(string jti, DateTimeOffset? expUtc)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        // Execute the transaction-based multi-use consumption logic
        var result = await conn.QuerySingleOrDefaultAsync<int?>(
            @"SET NOCOUNT ON;
BEGIN TRANSACTION;

UPDATE dbo.MultiUseLinks
SET ClicksLeft = ClicksLeft - 1
WHERE Jti=@jti AND ClicksLeft>0
    AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME());

IF @@ROWCOUNT = 0
BEGIN
    ROLLBACK TRANSACTION;
    SELECT CAST(NULL AS INT) AS Remaining;
    RETURN;
END;

DELETE FROM dbo.MultiUseLinks WHERE Jti=@jti AND ClicksLeft=0;

COMMIT TRANSACTION;

SELECT COALESCE((SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti=@jti), 0) AS Remaining;",
            new { jti });

        return result;
    }
}
