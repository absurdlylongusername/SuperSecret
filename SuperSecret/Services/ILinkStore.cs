using NUlid;
using SuperSecret.Models;

namespace SuperSecret.Services;

public interface ILinkStore
{
    Task CreateAsync(SecretLinkClaims claims);
    Task<bool> ConsumeSingleUseAsync(Ulid jti, DateTimeOffset? expUtc);
    Task<int?> ConsumeMultiUseAsync(Ulid jti, DateTimeOffset? expUtc); 
}
