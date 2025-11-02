namespace SuperSecret.Infrastructure;

public sealed class TokenOptions
{
    public string? TokenSigningKey { get; set; }

    public int MaxTTLInMinutes { get; set;  } = 43200; // 30 days
    public int MaxClicks { get; set; } = 100;
}