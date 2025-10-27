using NUlid;

namespace SuperSecret.Models;

public record SecretLinkClaims(string Sub, Ulid Jti, int? Max, DateTimeOffset? Exp, int Ver = 1);
