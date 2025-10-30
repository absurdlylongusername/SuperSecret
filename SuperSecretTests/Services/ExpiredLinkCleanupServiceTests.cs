using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SuperSecret.Infrastructure;
using SuperSecret.Services;

namespace SuperSecretTests.Services;

[TestOf(typeof(ExpiredLinkCleanupService))]
[Category("Unit")]
public class ExpiredLinkCleanupServiceTests
{
    // TODO: Finish these tests
    private Mock<ILogger<ExpiredLinkCleanupService>> _mockLogger = null!;
    private Mock<ILinkCleanupService> _mockCleanupService = null!;
    private IOptions<CleanupOptions> _options = null!;
    private ExpiredLinkCleanupService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<ExpiredLinkCleanupService>>(MockBehavior.Strict);
        _mockCleanupService = new Mock<ILinkCleanupService>(MockBehavior.Strict);
        _options = Options.Create(new CleanupOptions { IntervalSeconds = 1 }); // Short interval for testing
        _service = new ExpiredLinkCleanupService(_mockLogger.Object, _mockCleanupService.Object, _options);
    }

    [TearDown]
    public void TearDown()
    {
        _mockLogger.Reset();
        _mockCleanupService.Reset();
        _service.Dispose();
    }

    // ---------- Happy Paths ----------

    [Test]
    public async Task ExecuteAsync_RunsCleanupImmediatelyOnStart()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        _mockCleanupService.Setup(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupLogger("starting");
        SetupLogger("stopping");

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(150);
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _mockCleanupService.Verify(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task ExecuteAsync_LogsStartAndStop()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        _mockCleanupService.Setup(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var startLogged = false;
        var stopLogged = false;

        SetupLogger("starting", () => startLogged = true);
        SetupLogger("stopping", () => stopLogged = true);

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(150);
        await _service.StopAsync(cts.Token);
        await Task.Delay(200); // Allow time for stop log

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(startLogged, Is.True);
            Assert.That(stopLogged, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_RunsCleanupPeriodically()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(2500); // Run long enough for multiple cycles

        var callCount = 0;
        _mockCleanupService.Setup(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(Task.CompletedTask);

        SetupLogger("starting");
        SetupLogger("stopping");

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(2600);
        await _service.StopAsync(CancellationToken.None);

        // Assert - With 1 second interval and 2.5 second run, should have at least 2 calls (initial + 1-2 periodic)
        Assert.That(callCount, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task ExecuteAsync_PropagatesExceptionsFromCleanup()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        _mockCleanupService.Setup(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cleanup failed"));

        SetupLogger("starting");
        SetupLogger("stopping");

        // Act & Assert - Should not throw, service should continue
        await _service.StartAsync(cts.Token);
        await Task.Delay(150);
        await _service.StopAsync(CancellationToken.None);

        // Verify cleanup was attempted at least once
        _mockCleanupService.Verify(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ---------- Edge Cases ----------

    [Test]
    public async Task ExecuteAsync_LogsIntervalOnStart()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        _mockCleanupService.Setup(c => c.CleanupExpiredLinksAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var intervalLogged = false;
        SetupLogger("interval:", () => intervalLogged = true);
        SetupLogger("stopping");

        // Act
        await _service.StartAsync(cts.Token);
        await Task.Delay(50);
        await _service.StopAsync(CancellationToken.None);

        // Assert
        Assert.That(intervalLogged, Is.True);
    }

    // ---------- Helper Methods ----------

    private void SetupLogger(string containsText, Action? callback = null)
    {
        var setup = _mockLogger.Setup(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(containsText)),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()));

        if (callback != null)
            setup.Callback(callback);
    }
}