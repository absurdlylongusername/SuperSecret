using Microsoft.Extensions.Options;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;
using SuperSecretTests.TestInfrastructure;
using System.Data;

namespace SuperSecretTests;

[TestOf(nameof(SqlLinkStore))]
[Category("Integration")]
public class SqlLinkStoreIntegrationTests : DatabaseIntegrationTestBase
{
    private const string DefaultUsername = "user";

    private SqlLinkStore _store = null!;
    private readonly int maxExpiryInMinutes = 60;

    [OneTimeSetUp]
    public async Task OneTimeSetUp_DatabaseObjects()
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        await RequireTableExists(conn, DbObjects.Tables.SingleUseLinks);
        await RequireTableExists(conn, DbObjects.Tables.MultiUseLinks);
        await RequireProcExists(conn, DbObjects.Procs.CreateSingleUseLink);
        await RequireProcExists(conn, DbObjects.Procs.CreateMultiUseLink);
        await RequireProcExists(conn, DbObjects.Procs.ConsumeSingleUseLink);
        await RequireProcExists(conn, DbObjects.Procs.ConsumeMultiUseLink);
    }

    [SetUp]
    public void SetUp()
    {
        var tokenOptions = Options.Create(new TokenOptions { MaxTTLInMinutes = maxExpiryInMinutes});
        _store = new SqlLinkStore(_connectionFactory, tokenOptions);
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
            Assert.That(await CountSingleUseAsync(jti), Is.EqualTo(1));
            Assert.That(await CountMultiUseAsync(jti), Is.EqualTo(0));
        });

        // Cleanup
        await DeleteByJtiAsync(jti);
    }

    [Test]
    public async Task CreateAsync_HasMaxExpiry_WhenExpiryIsNull()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);

        var expectedExpiryDate = DateTimeOffset.UtcNow.AddMinutes(maxExpiryInMinutes);

        // Act
        await _store.CreateAsync(claims);

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.That(await CountSingleUseAsync(jti), Is.EqualTo(1));
            Assert.That(await GetExpiryDateAsync(jti), Is.EqualTo(expectedExpiryDate).Within(2).Seconds);
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
            Assert.That(await CountSingleUseAsync(jti), Is.EqualTo(0));
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
            Assert.That(await CountSingleUseAsync(jti), Is.EqualTo(1));
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
        var clicksLeft = await GetMultiUseClicksLeftAsync(jti);
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
        var remaining = await GetMultiUseClicksLeftAsync(jti);
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
        var clicksLeft = await GetMultiUseClicksLeftAsync(jti);
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
        var remaining = await GetMultiUseClicksLeftAsync(jti);
        Assert.That(remaining, Is.Null);
    }
}