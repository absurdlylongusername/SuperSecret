using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Validators;
using System.Data;

namespace SuperSecretTests.TestInfrastructure;

public abstract class DatabaseIntegrationTestBase
{
    protected IDbConnectionFactory _connectionFactory = null!;
    protected string ConnectionString { get; } = TestConfiguration.Options.ConnectionString;

    [OneTimeSetUp]
    public async Task BaseOneTimeSetUp()
    {
        _connectionFactory = new SqlConnectionFactory(Options.Create(new DatabaseOptions
        {
            ConnectionString = ConnectionString
        }));

        // Validate connectivity once
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.QuerySingleAsync<int>("SELECT 1");
    }

    // ---------- Existence checks (OBJECT_ID) ----------

    protected static async Task RequireTableExists(SqlConnection conn, string fullyQualifiedTableName)
    {
        const string sql = """
            SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NOT NULL THEN 1 ELSE 0 END
            """;
        var exists = await conn.QuerySingleAsync<int>(sql, new { name = fullyQualifiedTableName }) == 1;
        if (!exists)
            Assert.Inconclusive($"Table {fullyQualifiedTableName} not found. Deploy schema before running integration tests.");
    }

    protected static async Task RequireProcExists(SqlConnection conn, string fullyQualifiedProcName)
    {
        const string sql = """
            SELECT CASE WHEN OBJECT_ID(@name, N'P') IS NOT NULL THEN 1 ELSE 0 END
            """;
        var exists = await conn.QuerySingleAsync<int>(sql, new { name = fullyQualifiedProcName }) == 1;
        if (!exists)
            Assert.Inconclusive($"Stored procedure {fullyQualifiedProcName} not found. Deploy procedures before running integration tests.");
    }

    // ---------- Data helpers ----------

    protected async Task<int> CountSingleUseAsync(Ulid jti)
    {
        var sql = $"SELECT COUNT(*) FROM {DbObjects.Tables.SingleUseLinks} WHERE Jti = @jti";
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(sql, new { jti = jti.ToByteArray() });
    }

    protected async Task<int> CountMultiUseAsync(Ulid jti)
    {
        var sql = $"SELECT COUNT(*) FROM {DbObjects.Tables.MultiUseLinks} WHERE Jti = @jti";
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(sql, new { jti = jti.ToByteArray() });
    }

    protected async Task<int?> GetMultiUseClicksLeftAsync(Ulid jti)
    {
        var sql = $"SELECT ClicksLeft FROM {DbObjects.Tables.MultiUseLinks} WHERE Jti = @jti";
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<int?>(sql, new { jti = jti.ToByteArray() });
    }

    protected async Task CreateSingleUseAsync(Ulid jti, DateTime? expiresAtUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync(
            DbObjects.Procs.CreateSingleUseLink,
            new { jti = jti.ToByteArray(), expiresAt = expiresAtUtc },
            commandType: CommandType.StoredProcedure);
    }

    protected async Task CreateMultiUseAsync(Ulid jti, int clicksLeft, DateTime? expiresAtUtc)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync(
            DbObjects.Procs.CreateMultiUseLink,
            new { jti = jti.ToByteArray(), clicksLeft, expiresAt = expiresAtUtc },
            commandType: CommandType.StoredProcedure);
    }

    protected async Task DeleteByJtiAsync(Ulid jti)
    {
        var cleanupSql = $"""
            DELETE FROM {DbObjects.Tables.SingleUseLinks} WHERE Jti = @jti;
            DELETE FROM {DbObjects.Tables.MultiUseLinks} WHERE Jti = @jti;
            """;
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync(cleanupSql, new { jti = jti.ToByteArray() });
    }

    // ---------- Logging helper ----------

    protected static void SetupLogger<T>(Mock<ILogger<T>> loggerMock, string containsText, Action? callback = null)
    {
        var setup = loggerMock
            .Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(containsText)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

        if (callback != null)
            setup.Callback(callback);
    }

    // ---------- Test fakes ----------

    protected sealed class ThrowingConnectionFactory : IDbConnectionFactory
    {
        public Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated connection failure");
    }
}