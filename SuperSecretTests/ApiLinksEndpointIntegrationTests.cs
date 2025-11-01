using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUlid;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;
using SuperSecret.Validators;
using SuperSecretTests.TestInfrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SuperSecretTests;

[TestOf(typeof(Program))]
[Category("Integration")]
public class ApiLinksEndpointIntegrationTests : DatabaseIntegrationTestBase
{
    private const string DefaultUsername = "user";
    private const string ApiEndpoint = "/api/links";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private ITokenService TokenService => _factory.Services.GetRequiredService<ITokenService>();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await RequireTableExists(conn, DbObjects.Tables.SingleUseLinks);
        await RequireTableExists(conn, DbObjects.Tables.MultiUseLinks);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                        ["TokenSigningKey"] = "test-key-that-is-at-least-32-characters-long-for-hmac"
                    });
                });
            });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory?.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
    }

    // ---------- Happy Path - Single-use ----------


    private static IEnumerable<TestCaseData> ValidExpiryDateTestCases()
    {
        yield return new TestCaseData((DateTimeOffset?)null);
        yield return new TestCaseData(DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [TestCaseSource(nameof(ValidExpiryDateTestCases))]
    public async Task PostLinks_CreatesValidSingleUseLink_WithValidRequests(DateTimeOffset? expiryDate)
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, expiryDate);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Url, Does.StartWith(_client.BaseAddress!.Scheme));
            Assert.That(result.Url, Does.Contain("/supersecret/"));
        });

        // Cleanup
        await CleanupLinkFromUrl(result.Url);
    }

    [Test]
    public async Task PostLinks_ReturnedLinkIsAccessibleOnce_ThenFails()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);
        var createResponse = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var linkPath = new Uri(result!.Url).PathAndQuery;

        // Act - First access
        var firstResponse = await _client.GetAsync(linkPath);
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        // Act - Second access
        var secondResponse = await _client.GetAsync(linkPath);
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(firstContent, Does.Contain($"You have found the secret, {DefaultUsername}!"));
            Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(secondContent, Does.Contain("There are no secrets here"));
        });
    }

    // ---------- Happy Path - Multi-use ----------


    [TestCaseSource(nameof(ValidExpiryDateTestCases))]
    public async Task PostLinks_CreatesValidMultiUseLink_WithValidRequests(DateTimeOffset? expiryDate)
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 3, expiryDate);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result, Is.Not.Null);

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    [Test]
    public async Task PostLinks_ReturnedLinkIsAccessibleMultipleTimes_UntilMaxReached()
    {
        // Arrange
        const int maxClicks = 3;
        var request = new CreateLinkRequest(DefaultUsername, maxClicks, null);
        var createResponse = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var linkPath = new Uri(result!.Url).PathAndQuery;

        // Act - Access link maxClicks times
        var responses = new List<string>();
        for (int i = 0; i < maxClicks; i++)
        {
            var response = await _client.GetAsync(linkPath);
            var content = await response.Content.ReadAsStringAsync();
            responses.Add(content);
        }

        // Act - Access one more time (should fail)
        var failResponse = await _client.GetAsync(linkPath);
        var failContent = await failResponse.Content.ReadAsStringAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(responses, Has.All.Contains($"You have found the secret, {DefaultUsername}!"));
            Assert.That(failContent, Does.Contain("There are no secrets here"));
        });
    }

    // ---------- Happy Path - URL Generation ----------

    [Test]
    public async Task PostLinks_ReturnsCorrectUrlFormat()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result!.Url, Does.Match(@"^https?://[^/]+/supersecret/[A-Za-z0-9_\-\.]+$"));

        // Cleanup
        await CleanupLinkFromUrl(result.Url);
    }

    [Test]
    public async Task PostLinks_GeneratesUniqueTokens_ForMultipleRequests()
    {
        // Arrange
        var request1 = new CreateLinkRequest(DefaultUsername, 1, null);
        var request2 = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response1 = await _client.PostAsJsonAsync(ApiEndpoint, request1);
        var response2 = await _client.PostAsJsonAsync(ApiEndpoint, request2);

        // Assert
        var result1 = await response1.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result1!.Url, Is.Not.EqualTo(result2!.Url));

        // Cleanup
        await CleanupLinkFromUrl(result1.Url);
        await CleanupLinkFromUrl(result2.Url);
    }

    // ---------- Request Validation - Username ----------
    

    [TestCaseSource(typeof(TestCases), nameof(TestCases.InvalidUsernameTestCases))]
    public async Task PostLinks_ReturnsBadRequest_WhenInvalidUsername(string username, string expectedMessage)
    {
        // Arrange
        var request = new CreateLinkRequest(username, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain(expectedMessage));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenUsernameExactly50Characters()
    {
        // Arrange
        var request = new CreateLinkRequest(new string('a', 50), 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenUsernameExactly1Character()
    {
        // Arrange
        var request = new CreateLinkRequest("a", 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenUsernameAlphanumeric()
    {
        // Arrange
        var request = new CreateLinkRequest("User123ABC", 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    // ---------- Request Validation - Max ----------

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenMaxIsZero()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 0, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenMaxIsNegative()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, -5, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenMaxIsOne()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenMaxIsInt32MaxValue()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, int.MaxValue, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    // ---------- Request Validation - ExpiresAt ----------

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenExpiresAtInPast()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, DateTimeOffset.UtcNow.AddMinutes(-5));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenExpiresAtIsExactlyNow()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, DateTimeOffset.UtcNow);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenExpiresAtInFuture()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, DateTimeOffset.UtcNow.AddMinutes(1));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenExpiresAtNull()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();

        // Cleanup
        await CleanupLinkFromUrl(result!.Url);
    }

    // ---------- Edge Cases - Database Persistence ----------

    [Test]
    public async Task PostLinks_LinkPersistsInDatabase_SingleUse()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var jti = ExtractJtiFromUrl(result!.Url);

        // Assert
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        var count = await conn.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.SingleUseLinks WHERE Jti = @jti",
            new { jti = jti.ToByteArray() });
        Assert.That(count, Is.EqualTo(1));

        // Cleanup
        await CleanupByJti(jti);
    }

    [Test]
    public async Task PostLinks_LinkPersistsInDatabase_MultiUse()
    {
        // Arrange
        var maxClicks = 5;
        var request = new CreateLinkRequest(DefaultUsername, maxClicks, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var jti = ExtractJtiFromUrl(result!.Url);

        // Assert
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        var clicksLeft = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT ClicksLeft FROM dbo.MultiUseLinks WHERE Jti = @jti",
            new { jti = jti.ToByteArray() });
        Assert.That(clicksLeft, Is.EqualTo(maxClicks));

        // Cleanup
        await CleanupByJti(jti);
    }

    // ---------- Edge Cases - Token Validation ----------

    [Test]
    public async Task PostLinks_GeneratedTokenIsValid_CanBeDecoded()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var token = ExtractTokenFromUrl(result!.Url);

        // Act - Validate token using TokenService
        var claims = TokenService.Validate(token);

        // Assert
        Assert.That(claims, Is.Not.Null);

        // Cleanup
        await CleanupByJti(claims.Jti);
    }

    [Test]
    public async Task PostLinks_TokenContainsCorrectClaims()
    {
        // Arrange
        var username = "TestUser123";
        var maxClicks = 7;
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
        var request = new CreateLinkRequest(username, maxClicks, expiry);
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var token = ExtractTokenFromUrl(result!.Url);

        // Act - Validate and extract claims
        var claims = TokenService.Validate(token);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(claims, Is.Not.Null);
            Assert.That(claims!.Sub, Is.EqualTo(username));
            Assert.That(claims.Max, Is.EqualTo(maxClicks));
            Assert.That(claims.Exp, Is.Not.Null);
            // Allow 1 second tolerance for test execution time
            Assert.That(claims.Exp!.Value, Is.EqualTo(expiry).Within(TimeSpan.FromSeconds(1)));
        });

        // Cleanup
        await CleanupByJti(claims.Jti);
    }

    // ---------- Edge Cases - Concurrency ----------

    [Test]
    public async Task PostLinks_HandlesMultipleConcurrentRequests()
    {
        // Arrange
        const int requestCount = 10;
        var requests = Enumerable.Range(0, requestCount)
            .Select(i => new CreateLinkRequest($"User{i}", 1, null))
            .ToArray();

        // Act
        var tasks = requests.Select(req => _client.PostAsJsonAsync(ApiEndpoint, req)).ToArray();
        var responses = await Task.WhenAll(tasks);

        // Assert
        var results = await Task.WhenAll(
            responses.Select(r => r.Content.ReadFromJsonAsync<CreateLinkResponse>()));

        Assert.Multiple(() =>
        {
            Assert.That(responses.Select(m => m.StatusCode), Has.All.EqualTo(HttpStatusCode.OK));
            Assert.That(results, Has.All.Not.Null);
            Assert.That(results.Select(r => r!.Url).Distinct().Count(), Is.EqualTo(requestCount));
        });

        // Cleanup
        foreach (var result in results)
        {
            await CleanupLinkFromUrl(result!.Url);
        }
    }

    // ---------- Robustness - Malformed Requests ----------

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenRequestBodyMissing()
    {
        // Arrange & Act
        var response = await _client.PostAsync(ApiEndpoint, new StringContent("", Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenRequestBodyInvalidJson()
    {
        // Arrange & Act
        var response = await _client.PostAsync(ApiEndpoint,
            new StringContent("{invalid json", Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenContentTypeNotJson()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new CreateLinkRequest(DefaultUsername, 1, null));

        // Act
        var response = await _client.PostAsync(ApiEndpoint,
            new StringContent(json, Encoding.UTF8, "text/plain"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
    }

    // ---------- Security ----------

    [Test]
    public async Task PostLinks_TokenSignature_IsUniquePerRequest()
    {
        // Arrange
        var request1 = new CreateLinkRequest(DefaultUsername, 1, null);
        var request2 = new CreateLinkRequest(DefaultUsername, 1, null);

        // Act
        var response1 = await _client.PostAsJsonAsync(ApiEndpoint, request1);
        var response2 = await _client.PostAsJsonAsync(ApiEndpoint, request2);

        // Assert
        var result1 = await response1.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var token1 = ExtractTokenFromUrl(result1!.Url);
        var token2 = ExtractTokenFromUrl(result2!.Url);

        Assert.That(token1, Is.Not.EqualTo(token2));

        // Cleanup
        await CleanupLinkFromUrl(result1.Url);
        await CleanupLinkFromUrl(result2.Url);
    }

    [Test]
    public async Task PostLinks_ManipulatedToken_CannotBeUsed()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);
        var createResponse = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var originalToken = ExtractTokenFromUrl(result!.Url);

        // Manipulate token (change last character)
        var manipulatedToken = originalToken[..^1] + "X";
        var manipulatedPath = $"/supersecret/{manipulatedToken}";

        // Act
        var response = await _client.GetAsync(manipulatedPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.That(content, Does.Contain("There are no secrets here"));

        // Cleanup
        await CleanupLinkFromUrl(result.Url);
    }

    // ---------- Integration with SuperSecret Page ----------


    [Test]
    public async Task PostLinks_GeneratedLinkWithExpiry_FailsAfterExpiryDate()
    {
        // Arrange
        var expiry = DateTimeOffset.UtcNow.AddSeconds(2);
        var request = new CreateLinkRequest(DefaultUsername, 5, expiry);
        var createResponse = await _client.PostAsJsonAsync(ApiEndpoint, request);
        var result = await createResponse.Content.ReadFromJsonAsync<CreateLinkResponse>();
        var linkPath = new Uri(result!.Url).PathAndQuery;

        // Wait for expiry
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Act
        var response = await _client.GetAsync(linkPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(content, Does.Contain("There are no secrets here"));
        });
    }

    // ---------- Helpers ----------
    private async Task CleanupLinkFromUrl(string url)
    {
        var jti = ExtractJtiFromUrl(url);
        await CleanupByJti(jti);
    }

    private async Task CleanupByJti(Ulid jti)
    {
        const string cleanupSql = """
            DELETE FROM dbo.SingleUseLinks WHERE Jti = @jti;
            DELETE FROM dbo.MultiUseLinks WHERE Jti = @jti;
            """;
        await using var conn = await _connectionFactory.CreateOpenConnectionAsync();
        await conn.ExecuteAsync(cleanupSql, new { jti = jti.ToByteArray() });
    }

    private Ulid ExtractJtiFromUrl(string url)
    {
        var token = ExtractTokenFromUrl(url);
        var claims = TokenService.Validate(token);
        return claims!.Jti;
    }

    private static string ExtractTokenFromUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.Segments;
        return segments[^1];
    }
}
