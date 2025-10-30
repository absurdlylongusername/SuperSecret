using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Moq;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Services;
using SuperSecretTests.TestInfrastructure;

namespace SuperSecretTests;

[TestOf(typeof(LinkCleanupService))]
[Category("Integration")]
public class LinkCleanupServiceIntegrationTests : DatabaseIntegrationTestBase
{
    private LinkCleanupService _service = null!;
    private Mock<ILogger<LinkCleanupService>> _mockLogger = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp_DatabaseObjects()
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();

        await RequireTableExists(conn, DbObjects.Tables.SingleUseLinks);
        await RequireTableExists(conn, DbObjects.Tables.MultiUseLinks);
        await RequireProcExists(conn, DbObjects.Procs.CreateSingleUseLink);
        await RequireProcExists(conn, DbObjects.Procs.CreateMultiUseLink);
    }

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<LinkCleanupService>>(MockBehavior.Strict);
        _service = new LinkCleanupService(_mockLogger.Object, _connectionFactory);
    }

    [TearDown]
    public void TearDown()
    {
        _mockLogger.Reset();
    }

    [Test]
    public async Task CleanupExpiredLinksAsync_DeletesOnlyExpired_FromBothTables_AndLogs()
    {
        // Arrange
        var expiredSingle = Ulid.NewUlid();
        var futureSingle = Ulid.NewUlid();
        var nullExpSingle = Ulid.NewUlid();

        var expiredMulti1 = Ulid.NewUlid();
        var expiredMulti2 = Ulid.NewUlid();
        var futureMulti = Ulid.NewUlid();
        var nullExpMulti = Ulid.NewUlid();

        await CreateSingleUseAsync(expiredSingle, DateTime.UtcNow.AddMinutes(-2));
        await CreateSingleUseAsync(futureSingle, DateTime.UtcNow.AddMinutes(10));
        await CreateSingleUseAsync(nullExpSingle, null);

        await CreateMultiUseAsync(expiredMulti1, 3, DateTime.UtcNow.AddMinutes(-5));
        await CreateMultiUseAsync(expiredMulti2, 2, DateTime.UtcNow.AddMinutes(-1));
        await CreateMultiUseAsync(futureMulti, 5, DateTime.UtcNow.AddMinutes(20));
        await CreateMultiUseAsync(nullExpMulti, 7, null);

        var startedLogged = false;
        var deletedLogged = false;
        var noneLogged = false;
        SetupLogger(_mockLogger, "Starting expired link cleanup", () => startedLogged = true);
        SetupLogger(_mockLogger, "Deleted", () => deletedLogged = true);
        SetupLogger(_mockLogger, "No expired links found to delete", () => noneLogged = true);

        // Act
        await _service.CleanupExpiredLinksAsync(CancellationToken.None);

        // Assert rows deleted/kept as expected
        Assert.Multiple(async () =>
        {
            // SingleUse
            Assert.That(await CountSingleUseAsync(expiredSingle), Is.EqualTo(0));
            Assert.That(await CountSingleUseAsync(futureSingle), Is.EqualTo(1));
            Assert.That(await CountSingleUseAsync(nullExpSingle), Is.EqualTo(1));

            // MultiUse
            Assert.That(await CountMultiUseAsync(expiredMulti1), Is.EqualTo(0));
            Assert.That(await CountMultiUseAsync(expiredMulti2), Is.EqualTo(0));
            Assert.That(await CountMultiUseAsync(futureMulti), Is.EqualTo(1));
            Assert.That(await CountMultiUseAsync(nullExpMulti), Is.EqualTo(1));
        });

        Assert.Multiple(() =>
        {
            Assert.That(startedLogged, Is.True);
            Assert.That(deletedLogged || noneLogged, Is.True, "Expected a deletion or 'no expired' log");
        });

        // Cleanup remaining rows created by this test
        await DeleteByJtiAsync(futureSingle);
        await DeleteByJtiAsync(nullExpSingle);
        await DeleteByJtiAsync(futureMulti);
        await DeleteByJtiAsync(nullExpMulti);
    }

    [Test]
    public void CleanupExpiredLinksAsync_OnError_ThrowsAndLogsError()
    {
        // Arrange: use a factory that throws to simulate DB failure
        var throwingFactory = new ThrowingConnectionFactory();
        var svc = new LinkCleanupService(_mockLogger.Object, throwingFactory);

        var startedLogged = false;
        var errorLogged = false;
        SetupLogger(_mockLogger, "Starting expired link cleanup", () => startedLogged = true);
        SetupLogger(_mockLogger, "Error occurred while cleaning up expired links", () => errorLogged = true);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => svc.CleanupExpiredLinksAsync(CancellationToken.None));
        Assert.That(errorLogged, Is.True);
        Assert.That(startedLogged, Is.True);
    }

}