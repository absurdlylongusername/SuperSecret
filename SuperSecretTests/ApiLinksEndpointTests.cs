using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUlid;
using SuperSecret.Models;
using SuperSecret.Services;
using SuperSecret.Validators;
using SuperSecretTests.TestInfrastructure;
using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace SuperSecretTests;

[TestOf(typeof(Program))]
[Category("Api")]
public class ApiLinksEndpointTests
{
    private const string DefaultUsername = "user";
    private const string ApiEndpoint = "/api/links";

    private readonly Mock<ITokenService> _tokenServiceMock = new(MockBehavior.Strict);
    private readonly Mock<ILinkStore> _linkStoreMock = new(MockBehavior.Strict);
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove real services
                    var tokenServiceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITokenService));
                    if (tokenServiceDescriptor != null) services.Remove(tokenServiceDescriptor);

                    var linkStoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILinkStore));
                    if (linkStoreDescriptor != null) services.Remove(linkStoreDescriptor);

                    // Add mocks
                    services.AddSingleton(_tokenServiceMock.Object);
                    services.AddScoped(_ => _linkStoreMock.Object);
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
        _tokenServiceMock.Reset();
        _linkStoreMock.Reset();
    }



    // ---------- Request validation - Username ----------


    [TestCaseSource(typeof(TestCases), nameof(TestCases.InvalidUsernameTestCases))]
    public async Task PostLinks_ReturnsBadRequest_WhenInvalidUserName(string userName, string expectedErrorMessage)
    {
        // Arrange
        var request = new CreateLinkRequest(userName, 1, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain(expectedErrorMessage));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenUsernameExactly50Characters()
    {
        // Arrange
        var username = new string('a', 50);
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(username, jti, 1, null);
        var request = new CreateLinkRequest(username, 1, null);

        _tokenServiceMock.Setup(s => s.Create(username, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // ---------- Request validation - Max ----------

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenMaxIsZero()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 0, null);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain(ValidationMessages.MaxClicksMinimum));
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

    // ---------- Request validation - ExpiresAt ----------

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenExpiresAtInPast()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, DateTimeOffset.UtcNow.AddMinutes(-5));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(content, Does.Contain(ValidationMessages.ExpiryDateFuture));
    }

    [Test]
    public async Task PostLinks_ReturnsBadRequest_WhenExpiresAtIsNow()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, DateTimeOffset.UtcNow);

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenExpiresAtNull()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PostLinks_Succeeds_WhenExpiresAtInFuture()
    {
        // Arrange
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, exp);
        var request = new CreateLinkRequest(DefaultUsername, 1, exp);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, exp)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // ---------- Happy path ----------

    [Test]
    public async Task PostLinks_CreatesLink_WithValidRequest()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var claims = new SecretLinkClaims(DefaultUsername, jti, 3, exp);
        var request = new CreateLinkRequest(DefaultUsername, 3, exp);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 3, exp)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token-xyz");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Does.Contain("/supersecret/mock-token-xyz"));
    }

    [Test]
    public async Task PostLinks_CallsTokenServiceCreate_WithCorrectParameters()
    {
        // Arrange
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 5, exp);
        var request = new CreateLinkRequest(DefaultUsername, 5, exp);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 5, exp)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        _tokenServiceMock.VerifyAll();
    }

    [Test]
    public async Task PostLinks_CallsLinkStoreCreate_WithGeneratedClaims()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 2, null);
        var m = new SecretLinkClaims(DefaultUsername, jti, 2, null);
        var request = new CreateLinkRequest(DefaultUsername, 2, null);


        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 2, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(It.Is<SecretLinkClaims>(c => c == claims)))
            .Returns(Task.CompletedTask)
            .Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var result = await _client.PostAsJsonAsync(ApiEndpoint, request);

        //Assert.That(result.IsSuccessStatusCode);

        var jsonResponse = await result.Content.ReadAsStringAsync();

        // Assert
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    [Test]
    public async Task PostLinks_CallsTokenToJson_WithGeneratedClaims()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token").Verifiable(Times.Once());

        // Act
        await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        _tokenServiceMock.VerifyAll();
    }

    [Test]
    public async Task PostLinks_ReturnsOkWithUrl_WhenSuccessful()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("abc123");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Url, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task PostLinks_GeneratesCorrectUrl_WithSchemeAndHost()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("testtoken123");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        var result = await response.Content.ReadFromJsonAsync<CreateLinkResponse>();
        Assert.That(result!.Url, Does.Match(@"https?://[^/]+/supersecret/testtoken123"));
    }

    // ---------- Edge cases ----------

    [Test]
    public async Task PostLinks_HandlesMaxInt()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, int.MaxValue, null);
        var request = new CreateLinkRequest(DefaultUsername, int.MaxValue, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, int.MaxValue, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PostLinks_HandlesAlphanumericUsername()
    {
        // Arrange
        var username = "User123ABC";
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(username, jti, 1, null);
        var request = new CreateLinkRequest(username, 1, null);

        _tokenServiceMock.Setup(s => s.Create(username, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token");

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // ---------- Robustness ----------

    [Test]
    public async Task PostLinks_PropagatesTokenServiceExceptions()
    {
        // Arrange
        var request = new CreateLinkRequest(DefaultUsername, 1, null);
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null))
            .Throws(new InvalidOperationException("boom"));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task PostLinks_PropagatesLinkStoreExceptions()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }

    [Test]
    public async Task PostLinks_PropagatesTokenToJsonExceptions()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        var request = new CreateLinkRequest(DefaultUsername, 1, null);

        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims);
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask);
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Throws(new InvalidOperationException("boom"));

        // Act
        var response = await _client.PostAsJsonAsync(ApiEndpoint, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
    }
}