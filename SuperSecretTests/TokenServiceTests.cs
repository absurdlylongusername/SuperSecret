using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using NUlid;
using SuperSecret.Services;
using Microsoft.Extensions.Options;
using SuperSecret.Infrastructure;

namespace SuperSecretTests;

[TestOf(nameof(TokenService))]
public class TokenServiceTests
{
    private const string DefaultKey = "0123456789abcdef0123456789abcdef01234567";
    private TokenService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _service = CreateService(DefaultKey);
    }

    [Test]
    public void Create_SetsClaimsCorrectly()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var claims = _service.Create("alice", 3, expiration);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(claims.Sub, Is.EqualTo("alice"));
            Assert.That(claims.Max, Is.EqualTo(3));
            Assert.That(claims.Exp?.ToUnixTimeSeconds(), Is.EqualTo(expiration.ToUnixTimeSeconds()));
            Assert.That(claims.Ver, Is.GreaterThanOrEqualTo(1));
            Assert.That(claims.Jti, Is.Not.EqualTo(default(Ulid)));
        });
    }

    [Test]
    public void Token_Roundtrips_With_Validate()
    {
        // Arrange
        var expiration = DateTimeOffset.UtcNow.AddMinutes(5);
        var originalClaim = _service.Create("bob", 2, expiration);

        // Act
        var tokenString = _service.TokenToJson(originalClaim);
        var validatedClaims = _service.Validate(tokenString);

        // Assert
        Assert.That(validatedClaims, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(validatedClaims!.Sub, Is.EqualTo(originalClaim.Sub));
            Assert.That(validatedClaims.Jti, Is.EqualTo(originalClaim.Jti));
            Assert.That(validatedClaims.Max, Is.EqualTo(originalClaim.Max));
            Assert.That(validatedClaims.Exp?.ToUnixTimeSeconds(), Is.EqualTo(originalClaim.Exp?.ToUnixTimeSeconds()));
            Assert.That(validatedClaims.Ver, Is.EqualTo(originalClaim.Ver));
        });
    }

    [Test]
    public void Validate_ReturnsNull_When_Signature_Tampered()
    {
        // Arrange
        var tokenString = _service.TokenToJson(_service.Create("mallory", 1, DateTimeOffset.UtcNow.AddMinutes(5)));
        var parts = tokenString.Split('.');
        Assert.That(parts.Length, Is.EqualTo(3));

        // Act
        var signatureChars = parts[2].ToCharArray();
        signatureChars[0] = signatureChars[0] == 'A' ? 'B' : 'A'; // keep within Base64Url alphabet
        var tamperedTokenString = $"{parts[0]}.{parts[1]}.{new string(signatureChars)}";
        var validatedClaims = _service.Validate(tamperedTokenString);

        // Assert
        Assert.That(validatedClaims, Is.Null);
    }

    [Test]
    public void Validate_ReturnsNull_When_Expired()
    {
        // Arrange
        var expiredClaim = _service.Create("charlie", 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        var tokenString = _service.TokenToJson(expiredClaim);

        // Act
        var validatedClaims = _service.Validate(tokenString);

        // Assert
        Assert.That(validatedClaims, Is.Null);
    }

    [Test]
    public void Validate_ReturnsNull_When_Sub_Missing()
    {
        // Arrange
        var customKey = "abcdefghijklmnopqrstuvwxyz012345abcdefghijklmnopqrstuvwxyz";
        var serviceWithCustomKey = CreateService(customKey);
        var headerBase64 = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payloadJson = $"{{\"jti\":\"{Ulid.NewUlid()}\",\"ver\":1}}"; // no sub
        var payloadBase64 = Base64UrlEncode(payloadJson);
        var message = $"{headerBase64}.{payloadBase64}";
        var signatureBase64 = ComputeHmacSha256(message, customKey);
        var tokenString = $"{message}.{signatureBase64}";

        // Act
        var validatedClaims = serviceWithCustomKey.Validate(tokenString);

        // Assert
        Assert.That(validatedClaims, Is.Null);
    }

    [Test]
    public void Validate_ReturnsNull_When_Jti_Invalid()
    {
        // Arrange
        var customKey = "abcdefghijklmnopqrstuvwxyz012345abcdefghijklmnopqrstuvwxyz";
        var serviceWithCustomKey = CreateService(customKey);
        var headerBase64 = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payloadJson = "{\"sub\":\"dave\",\"jti\":\"not-a-ulid\",\"ver\":1}";
        var payloadBase64 = Base64UrlEncode(payloadJson);
        var message = $"{headerBase64}.{payloadBase64}";
        var signatureBase64 = ComputeHmacSha256(message, customKey);
        var tokenString = $"{message}.{signatureBase64}";

        // Act
        var validatedClaims = serviceWithCustomKey.Validate(tokenString);

        // Assert
        Assert.That(validatedClaims, Is.Null);
    }

    private static TokenService CreateService(string key)
    {
        var tokenOptions = new TokenOptions { TokenSigningKey = key };
        var options = Options.Create(tokenOptions);
        return new TokenService(options);
    }

    private static string Base64UrlEncode(string value)
        => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));

    private static string ComputeHmacSha256(string message, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return WebEncoders.Base64UrlEncode(signatureBytes);
    }
}