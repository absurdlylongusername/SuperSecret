using SuperSecret.Models;

namespace SuperSecret.Services;

public interface ITokenService
{
    SecretLinkClaims Create(string username, int max = 1, DateTimeOffset? expiresAt = null);
    string Pack(SecretLinkClaims claims);
    SecretLinkClaims? Validate(string token);
}
