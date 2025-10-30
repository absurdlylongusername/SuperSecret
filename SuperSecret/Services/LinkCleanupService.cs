using Dapper;
using SuperSecret.Infrastructure;
using System.Data;

namespace SuperSecret.Services;

public class LinkCleanupService : ILinkCleanupService
{
    private readonly ILogger<LinkCleanupService> _logger;
    private readonly IDbConnectionFactory _connectionFactory;

    public LinkCleanupService(ILogger<LinkCleanupService> logger, IDbConnectionFactory connectionFactory)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
    }

    public async Task CleanupExpiredLinksAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting expired link cleanup");

            await using var conn = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

            const string sql = """
                DELETE FROM dbo.SingleUseLinks 
                WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
                
                DELETE FROM dbo.MultiUseLinks 
                WHERE ExpiresAt IS NOT NULL AND ExpiresAt <= SYSUTCDATETIME();
                
                SELECT @@ROWCOUNT AS TotalDeleted;
                """;

            var rowsDeleted =
            await conn.QuerySingleAsync<int>("dbo.DeleteExpiredLinks",
                                             commandType: CommandType.StoredProcedure);

            if (rowsDeleted > 0)
            {
                _logger.LogInformation("Deleted {Count} expired links", rowsDeleted);
            }
            else
            {
                _logger.LogDebug("No expired links found to delete");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while cleaning up expired links");
            throw;
        }
    }
}