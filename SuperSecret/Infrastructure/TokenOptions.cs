namespace SuperSecret.Infrastructure;

public sealed class TokenOptions
{
    // Required: minimum 32 chars recommended
    public string? TokenSigningKey { get; set; }
}