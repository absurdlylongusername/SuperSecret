namespace SuperSecret.Models;

public record CreateLinkRequest(string Username = "", int Max = 1, DateTimeOffset? ExpiresAt = null);

public record CreateLinkResponse(string Url);
