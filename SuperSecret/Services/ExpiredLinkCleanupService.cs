using Microsoft.Extensions.Options;
using SuperSecret.Infrastructure;

namespace SuperSecret.Services;

public class ExpiredLinkCleanupService : BackgroundService
{
    private readonly ILogger<ExpiredLinkCleanupService> _logger;
    private readonly ILinkCleanupService _cleanupService;
    private readonly TimeSpan _cleanupInterval;

    public ExpiredLinkCleanupService(ILogger<ExpiredLinkCleanupService> logger,
                                     ILinkCleanupService cleanupService,
                                     IOptions<CleanupOptions> options)
    {
        _logger = logger;
        _cleanupService = cleanupService;
        _cleanupInterval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Expired Link Cleanup Service starting (interval: {Interval})", _cleanupInterval);

        try
        {
            // Run immediately on startup
            await _cleanupService.CleanupExpiredLinksAsync(stoppingToken);

            // If no periodic interval is configured, exit gracefully
            if (_cleanupInterval <= TimeSpan.Zero)
                return;

            using var timer = new PeriodicTimer(_cleanupInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _cleanupService.CleanupExpiredLinksAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown, swallow to allow graceful stop logging
        }
        finally
        {
            _logger.LogInformation("Expired Link Cleanup Service stopping");
        }
    }
}