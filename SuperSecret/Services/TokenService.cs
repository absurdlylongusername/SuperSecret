using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NUlid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SuperSecret.Infrastructure;
using SuperSecret.Models;

namespace SuperSecret.Services;

public class TokenService : ITokenService
{
    private const string Algorithm = "HS256";
    private readonly byte[] _signingKeyBytes;

    public TokenService(IOptions<TokenOptions> options)
    {
        var key = options.Value.TokenSigningKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"{nameof(TokenOptions.TokenSigningKey)} is not configured.");

        _signingKeyBytes = Encoding.UTF8.GetBytes(key);
    }

    public SecretLinkClaims Create(string username, int max = 1, DateTimeOffset? expiresAt = null)
    {
        return new SecretLinkClaims(username, Ulid.NewUlid(), max, expiresAt, 1);
    }

    public string TokenToJson(SecretLinkClaims claims)
    {
        var header = new JObject
        {
            ["alg"] = Algorithm,
            ["typ"] = "JWT"
        };
        var headerJson = header.ToString(Formatting.None);
        var headerBase64 = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));

        var payload = new JObject
        {
            ["sub"] = claims.Sub,
            ["jti"] = claims.Jti.ToString(),
            ["max"] = claims.Max is null ? null : JToken.FromObject(claims.Max),
            ["exp"] = claims.Exp?.ToUnixTimeSeconds() is long e ? (JToken)JToken.FromObject(e) : JValue.CreateNull(),
            ["ver"] = claims.Ver
        };
        var payloadJson = payload.ToString(Formatting.None);
        var payloadBase64 = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        var message = $"{headerBase64}.{payloadBase64}";
        var signature = ComputeSignature(message);
        var signatureBase64 = WebEncoders.Base64UrlEncode(signature);

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
            var providedSignature = WebEncoders.Base64UrlDecode(parts[2]);
            var computedSignature = ComputeSignature(message);

            if (!CryptographicOperations.FixedTimeEquals(providedSignature, computedSignature))
                return null;

            var payloadJson = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[1]));
            var root = JObject.Parse(payloadJson);

            var sub = (string?)root["sub"];
            if (string.IsNullOrWhiteSpace(sub))
                return null;

            var jtiString = (string?)root["jti"];
            if (string.IsNullOrWhiteSpace(jtiString) || !Ulid.TryParse(jtiString, out var jti))
                return null;

            var expToken = root["exp"];
            if (expToken != null && expToken.Type != JTokenType.Null)
            {
                var expSeconds = expToken.Value<long>();
                var exp = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                if (exp <= DateTimeOffset.UtcNow) return null;
            }

            int? max = null;
            var maxToken = root["max"];
            if (maxToken != null && maxToken.Type != JTokenType.Null)
                max = maxToken.Value<int>();

            DateTimeOffset? expiresAt = null;
            if (expToken != null && expToken.Type != JTokenType.Null)
            {
                var expSeconds = expToken.Value<long>();
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
            }

            int ver = root["ver"]?.Value<int>() ?? 1;

            return new SecretLinkClaims(sub, jti, max, expiresAt, ver);
        }
        catch
        {
            return null;
        }
    }

    private byte[] ComputeSignature(string message)
    {
        using var hmac = new HMACSHA256(_signingKeyBytes);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
    }
}