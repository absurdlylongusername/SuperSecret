using SuperSecret.Models;

namespace SuperSecret.Services;

public interface ILinkStore
{
    Task CreateAsync(SecretLinkClaims claims);
    Task<bool> ConsumeSingleUseAsync(string jti, DateTimeOffset? expUtc);
    Task<int?> ConsumeMultiUseAsync(string jti, DateTimeOffset? expUtc);
}
