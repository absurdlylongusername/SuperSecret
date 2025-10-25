using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SuperSecret.Models;

namespace SuperSecret.Services;

public class TokenService : ITokenService
{
    private readonly byte[] _signingKey;
    private const string Algorithm = "HS256";

    public TokenService(IConfiguration configuration)
    {
        var key = configuration["TokenSigningKey"] 
           ?? throw new InvalidOperationException("TokenSigningKey not configured");
        _signingKey = Encoding.UTF8.GetBytes(key);
    }

    public SecretLinkClaims Create(string username, int max = 1, DateTimeOffset? expiresAt = null)
    {
        return new SecretLinkClaims
        {
            Sub = username,
            Jti = GenerateId(),
            Max = max,
            Exp = expiresAt,
            Ver = 1
        };
    }

    public string Pack(SecretLinkClaims claims)
    {
        var header = new { alg = Algorithm, typ = "JWT" };
        var headerJson = JsonSerializer.Serialize(header);
        var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        var payload = new
        {
            sub = claims.Sub,
            jti = claims.Jti,
            max = claims.Max,
            exp = claims.Exp?.ToUnixTimeSeconds(),
            ver = claims.Ver
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var message = $"{headerBase64}.{payloadBase64}";
        var signature = ComputeSignature(message);
        var signatureBase64 = Base64UrlEncode(signature);

        return $"{message}.{signatureBase64}";
    }

    public SecretLinkClaims? Validate(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var message = $"{parts[0]}.{parts[1]}";
            var providedSignature = Base64UrlDecode(parts[2]);
            var computedSignature = ComputeSignature(message);

            // Constant-time comparison
            if (!CryptographicOperations.FixedTimeEquals(providedSignature, computedSignature))
                return null;

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
 
            if (payload == null)
                return null;

            // Check expiry if present
            if (payload.TryGetValue("exp", out var expElement) && expElement.ValueKind != JsonValueKind.Null)
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
                if (exp <= DateTimeOffset.UtcNow)
                    return null;
            }

            var claims = new SecretLinkClaims
            {
                Sub = payload["sub"].GetString() ?? "",
                Jti = payload["jti"].GetString() ?? "",
                Max = payload.TryGetValue("max", out var maxElement) && maxElement.ValueKind != JsonValueKind.Null
                    ? maxElement.GetInt32() 
                    : null,
                Exp = payload.TryGetValue("exp", out var expEl) && expEl.ValueKind != JsonValueKind.Null
                    ? DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64())
                    : null,
                Ver = payload.TryGetValue("ver", out var verElement) 
                    ? verElement.GetInt32() 
                    : 1
            };

            return claims;
        }
        catch
        {
            return null;
        }
    }

    private byte[] ComputeSignature(string message)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }

    private static string GenerateId()
    {
        // Generate a 26-character base32 ULID-style ID
        var guid = Guid.NewGuid();
        var bytes = guid.ToByteArray();
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=')
            .Substring(0, 26);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var base64 = input
            .Replace('-', '+')
            .Replace('_', '/');

        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        return Convert.FromBase64String(base64);
    }
}
