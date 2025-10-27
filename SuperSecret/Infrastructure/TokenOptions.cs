namespace SuperSecret.Infrastructure;

public sealed class TokenOptions
{
    // Required: minimum 32 chars recommended
    public string? SigningKey { get; set; }
}