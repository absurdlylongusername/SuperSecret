using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;

namespace SuperSecretTests;

[TestOf(nameof(SqlLinkStore))]
[Category("Integration")]
public class SqlLinkStoreIntegrationTests
{
    private const string DefaultUsername = "user";

    private string _connectionString = null!;
    private IDbConnectionFactory _connectionFactory = null!;
    private SqlLinkStore _store = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Build config with normal precedence: JSON < env vars < "command line" (NUnit parameters)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(TestContext.CurrentContext.TestDirectory)
            .AddJsonFile("testsettings.json", optional: true)
            .AddJsonFile($"testsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();


        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? throw new Exception("No connection string");

        _connectionFactory = new SqlConnectionFactory(Options.Create(new DatabaseOptions
        {
            ConnectionString = _connectionString
        }));

        _store = new SqlLinkStore(_connectionFactory);

        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        await RequireTableExists(conn, "dbo", "SingleUseLinks");
        await RequireTableExists(conn, "dbo", "MultiUseLinks");
        await RequireProcExists(conn, "dbo", "CreateSingleUseLink");
        await RequireProcExists(conn, "dbo", "CreateMultiUseLink");
        await RequireProcExists(conn, "dbo", "ConsumeSingleUseLink");
        await RequireProcExists(conn, "dbo", "ConsumeMultiUseLink");
    }

    // ---------------- CreateAsync (Single-use) ----------------

    private static IEnumerable<TestCaseData> CreateAsync_SingleUse_Cases()
    {
        yield return new TestCaseData(null, null);
        yield return new TestCaseData(1, null);
        yield return new TestCaseData(null, DateTimeOffset.UtcNow.AddMinutes(5));
        yield return new TestCaseData(1, DateTimeOffset.UtcNow.AddMinutes(5));
    }

    [Test]
    [TestCaseSource(nameof(CreateAsync_SingleUse_Cases))]
    public async Task CreateAsync_InsertsRow_WhenSingleUse(int? max, DateTimeOffset? exp)
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, max, exp);

        // Act
        await _store.CreateAsync(claims);

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.That(await CountAsync("SELECT COUNT(*) FROM dbo.SingleUseLinks WHERE Jti = @jti", jti), Is.EqualTo(1));
            Assert.That(await CountAsync("SELECT COUNT(*) FROM dbo.MultiUseLinks WHERE Jti = @jti", jti), Is.EqualTo(0));
        });

        // Cleanup
        await DeleteByJtiAsync(jti);
    }

    // ---------------- ConsumeSingleUseAsync ----------------

    [Test]
    public async Task ConsumeSingleUseAsync_SucceedsOnce_ThenFails()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, DateTimeOffset.UtcNow.AddMinutes(10));
        await _store.CreateAsync(claims);

        // Act
        var first = await _store.ConsumeSingleUseAsync(jti, claims.Exp);
        var second = await _store.ConsumeSingleUseAsync(jti, claims.Exp);

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(await CountAsync("SELECT COUNT(*) FROM dbo.SingleUseLinks WHERE Jti = @jti", jti), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ConsumeSingleUseAsync_DoesNotConsume_WhenExpired()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var expired = new SecretLinkClaims(DefaultUsername, jti, 1, DateTimeOffset.UtcNow.AddMinutes(-5));
        await _store.CreateAsync(expired);

        // Act
        var consumed = await _store.ConsumeSingleUseAsync(jti, expired.Exp);

        Assert.Multiple(async () =>
        {
            // Assert
            Assert.That(consumed, Is.False);
            Assert.That(await CountAsync("SELECT COUNT(*) FROM dbo.SingleUseLinks WHERE Jti = @jti", jti), Is.EqualTo(1));
        });

        // Cleanup
        await DeleteByJtiAsync(jti);
    }

    [Test]
    public async Task ConsumeSingleUseAsync_ReturnsFalse_ForNonexistent()
    {
        // Arrange
        var jti = Ulid.NewUlid();

        // Act
        var consumed = await _store.ConsumeSingleUseAsync(jti, null);

        // Assert
        Assert.That(consumed, Is.False);
    }

    // ---------------- CreateAsync (Multi-use) ----------------

    [Test]
    public async Task CreateAsync_InsertsRow_WhenMultiUse()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 3, DateTimeOffset.UtcNow.AddMinutes(10));

        // Act
        await _store.CreateAsync(claims);

        // Assert
        var clicksLeft = await QuerySingleOrDefaultAsync<int>("SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti", jti);
        Assert.That(clicksLeft, Is.EqualTo(3));

        // Cleanup
        await DeleteByJtiAsync(jti);
    }

    // ---------------- ConsumeMultiUseAsync ----------------

    [Test]
    public async Task ConsumeMultiUseAsync_DecrementsToZero_ThenNull()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 3, DateTimeOffset.UtcNow.AddMinutes(10));
        await _store.CreateAsync(claims);

        // Act
        var afterFirst = await _store.ConsumeMultiUseAsync(jti, claims.Exp);
        var afterSecond = await _store.ConsumeMultiUseAsync(jti, claims.Exp);
        var afterThird = await _store.ConsumeMultiUseAsync(jti, claims.Exp);
        var afterFourth = await _store.ConsumeMultiUseAsync(jti, claims.Exp);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.EqualTo(2));
            Assert.That(afterSecond, Is.EqualTo(1));
            Assert.That(afterThird, Is.EqualTo(0));
            Assert.That(afterFourth, Is.Null);
        });
        var remaining = await QuerySingleOrDefaultAsync<int?>("SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti", jti);
        Assert.That(remaining, Is.Null); // row deleted
    }

    [Test]
    public async Task ConsumeMultiUseAsync_DoesNotDecrement_WhenExpired()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var expired = new SecretLinkClaims(DefaultUsername, jti, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.CreateAsync(expired);

        // Act
        var afterAttempt = await _store.ConsumeMultiUseAsync(jti, expired.Exp);

        // Assert
        Assert.That(afterAttempt, Is.Null);
        var clicksLeft = await QuerySingleOrDefaultAsync<int?>("SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti", jti);
        Assert.That(clicksLeft, Is.EqualTo(2));

        // Cleanup
        await DeleteByJtiAsync(jti);
    }

    [Test]
    public async Task ConsumeMultiUseAsync_ReturnsNull_ForNonexistent()
    {
        // Arrange
        var jti = Ulid.NewUlid();

        // Act
        var result = await _store.ConsumeMultiUseAsync(jti, null);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ConsumeMultiUseAsync_IsSafeUnderConcurrency()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var maxClicks = 5;
        var claims = new SecretLinkClaims(DefaultUsername, jti, maxClicks, DateTimeOffset.UtcNow.AddMinutes(5));
        await _store.CreateAsync(claims);

        // Act
        var attempts = Enumerable.Range(0, maxClicks * 2)
            .Select(_ => _store.ConsumeMultiUseAsync(jti, claims.Exp))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        // Assert
        var successes = results.Count(r => r.HasValue);
        Assert.That(successes, Is.EqualTo(maxClicks));
        var remaining = await QuerySingleOrDefaultAsync<int?>("SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti", jti);
        Assert.That(remaining, Is.Null);
    }

    // ---------------- Helpers (Dapper) ----------------

    private async Task<int> CountAsync(string sql, Ulid jti)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        return await conn.QuerySingleAsync<int>(sql, new { jti = jti.ToByteArray() });
    }

    private async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, Ulid jti)
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<T>(sql, new { jti = jti.ToByteArray() });
    }

    private async Task DeleteByJtiAsync(Ulid jti)
    {
        const string cleanupSql = """
            DELETE FROM dbo.SingleUseLinks WHERE Jti = @jti;
            DELETE FROM dbo.MultiUseLinks WHERE Jti = @jti;
            """;
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync(cleanupSql, new { jti = jti.ToByteArray() });
    }

    private static async Task RequireTableExists(SqlConnection conn, string schema, string name)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = @schema AND t.name = @name
            ) THEN 1 ELSE 0 END
            """;
        var exists = await conn.QuerySingleAsync<int>(sql, new { schema, name }) == 1;
        if (!exists)
            Assert.Inconclusive($"Table {schema}.{name} not found. Deploy schema before running integration tests.");
    }

    private static async Task RequireProcExists(SqlConnection conn, string schema, string name)
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.procedures p
                JOIN sys.schemas s ON s.schema_id = p.schema_id
                WHERE s.name = @schema AND p.name = @name
            ) THEN 1 ELSE 0 END
            """;
        var exists = await conn.QuerySingleAsync<int>(sql, new { schema, name }) == 1;
        if (!exists)
            Assert.Inconclusive($"Stored procedure {schema}.{name} not found. Deploy procedures before running integration tests.");
    }
}