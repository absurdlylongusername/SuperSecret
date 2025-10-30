namespace SuperSecret.Services;

public interface ILinkCleanupService
{
    Task CleanupExpiredLinksAsync(CancellationToken cancellationToken);
}