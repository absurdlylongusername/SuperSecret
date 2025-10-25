namespace SuperSecret.Models;

public record SecretLinkClaims
{
    public required string Sub { get; init; }        // Username
    public required string Jti { get; init; }        // Unique ID
    public int? Max { get; init; }     // Max clicks (null or 1 = single use)
    public DateTimeOffset? Exp { get; init; }        // Expiry
    public int Ver { get; init; } = 1;      // Version
}
