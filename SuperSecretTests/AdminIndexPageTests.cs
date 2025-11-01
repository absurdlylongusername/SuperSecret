using System;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUlid;
using SuperSecret.Models;
using SuperSecret.Pages.Admin;
using SuperSecret.Services;

namespace SuperSecretTests;

[TestOf(nameof(IndexModel))]
public class AdminIndexPageTests
{
    private const string DefaultUsername = "user";

    private readonly Mock<ITokenService> _tokenServiceMock = new(MockBehavior.Strict);
    private readonly Mock<ILinkStore> _linkStoreMock = new(MockBehavior.Strict);
    private readonly Mock<IValidator<CreateLinkRequest>> _validatorMock = new(MockBehavior.Strict);
    private IndexModel _page;

    [SetUp]
    public void SetUp()
    {
        _page = CreatePage(_tokenServiceMock.Object, _linkStoreMock.Object, _validatorMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _tokenServiceMock.Reset();
        _linkStoreMock.Reset();
        _validatorMock.Reset();
        _page = default!;
    }

    // ---------- OnGet behavior ----------

    [Test]
    public void OnGet_InitializesInputWithDefaults()
    {
        // Arrange
        // Page already created in SetUp

        // Act
        _page.OnGet();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(_page.Input, Is.Not.Null);
            Assert.That(_page.Input.Username, Is.EqualTo(string.Empty));
            Assert.That(_page.Input.Max, Is.EqualTo(1));
            Assert.That(_page.Input.ExpiresAt, Is.Null);
            Assert.That(_page.GeneratedUrl, Is.Null);
        });
    }

    // ---------- OnPostAsync - Model validation ----------

    [Test]
    public async Task OnPostAsync_ReturnsPage_WhenModelStateInvalid()
    {
        // Arrange
        _page.ModelState.AddModelError("SomeKey", "Some error");

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<PageResult>());
        _tokenServiceMock.Verify(s => s.Create(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _linkStoreMock.Verify(s => s.CreateAsync(It.IsAny<SecretLinkClaims>()), Times.Never);
    }

    // ---------- OnPostAsync - Expiry validation ----------

    private static IEnumerable<TestCaseData> OnPostAsync_ReturnsPage_WhenInvalidExpiryDate_TestCases()
    {
        yield return new TestCaseData(DateTimeOffset.UtcNow);
        yield return new TestCaseData(DateTimeOffset.UtcNow.AddMinutes(-10));
    }

    [TestCaseSource(nameof(OnPostAsync_ReturnsPage_WhenInvalidExpiryDate_TestCases))]
    public async Task OnPostAsync_ReturnsPage_WhenInvalidExpiryDate(DateTimeOffset expiresAt)
    {
        // Arrange
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;
        _page.Input.ExpiresAt = expiresAt;

        var validationFailure = new ValidationFailure("ExpiresAt", "Expiry date must be in the future");
        var validationResult = new ValidationResult([validationFailure]);

        SetupValidator(_page.Input, validationResult, Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<PageResult>());
            Assert.That(_page.ModelState.IsValid, Is.False);
            Assert.That(_page.ModelState["ExpiresAt"]?.Errors, Has.Count.EqualTo(1));
        });
        Assert.That(_page.ModelState["ExpiresAt"]!.Errors[0].ErrorMessage,
            Is.EqualTo("Expiry date must be in the future"));
        _tokenServiceMock.Verify(s => s.Create(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _validatorMock.VerifyAll();
    }

    [Test]
    public async Task OnPostAsync_Succeeds_WhenExpiresAtNull()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;
        _page.Input.ExpiresAt = null;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token").Verifiable(Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.InstanceOf<PageResult>());
            Assert.That(_page.ModelState.IsValid, Is.True);
            Assert.That(_page.GeneratedUrl, Is.Not.Null);
        });
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    // ---------- OnPostAsync - Happy path (single-use) ----------

    [Test]
    public async Task OnPostAsync_CreatesSingleUse_WhenMaxIsOne()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, exp);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;
        _page.Input.ExpiresAt = exp;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, exp)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token").Verifiable(Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
        Assert.That(result, Is.InstanceOf<PageResult>());
    }

    [Test]
    public async Task OnPostAsync_CallsLinkStoreCreate_WithGeneratedClaims()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var claims = new SecretLinkClaims(DefaultUsername, jti, 2, exp);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 2;
        _page.Input.ExpiresAt = exp;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 2, exp)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(It.Is<SecretLinkClaims>(c => c == claims)))
            .Returns(Task.CompletedTask)
            .Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token").Verifiable(Times.Once());

        // Act
        await _page.OnPostAsync();

        // Assert
        _validatorMock.VerifyAll();
        _linkStoreMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
    }

    [Test]
    public async Task OnPostAsync_GeneratesTokenFromClaims()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;
        _page.Input.ExpiresAt = null;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token").Verifiable(Times.Once());

        // Act
        await _page.OnPostAsync();

        // Assert
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    // ---------- OnPostAsync - Happy path (multi-use) ----------

    [Test]
    public async Task OnPostAsync_CreatesMultiUse_WhenMaxGreaterThanOne()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(10);
        var claims = new SecretLinkClaims(DefaultUsername, jti, 5, exp);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 5;
        _page.Input.ExpiresAt = exp;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 5, exp)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("mock-token").Verifiable(Times.Once());

        // Act
        await _page.OnPostAsync();

        // Assert
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    // ---------- OnPostAsync - URL generation ----------

    [Test]
    public async Task OnPostAsync_SetsGeneratedUrl_WithCorrectSchemeHostAndToken()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;
        _page.Input.ExpiresAt = null;

        _page.HttpContext.Request.Scheme = "https";
        _page.HttpContext.Request.Host = new HostString("localhost", 5001);

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("abc123token").Verifiable(Times.Once());

        // Act
        await _page.OnPostAsync();

        // Assert
        Assert.That(_page.GeneratedUrl, Is.EqualTo("https://localhost:5001/supersecret/abc123token"));
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    [Test]
    public async Task OnPostAsync_ReturnsPageResult_AfterSuccess()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token").Verifiable(Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<PageResult>());
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    // ---------- OnPostAsync - Edge cases ----------

    [Test]
    public async Task OnPostAsync_HandlesVeryLongUsername()
    {
        // Arrange
        var username = new string('a', 50);
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(username, jti, 1, null);
        _page.Input.Username = username;
        _page.Input.Max = 1;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(username, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token").Verifiable(Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(result, Is.InstanceOf<PageResult>());
            Assert.That(_page.ModelState.IsValid, Is.True);
        });
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    [Test]
    public async Task OnPostAsync_HandlesMaxInt()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, int.MaxValue, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = int.MaxValue;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, int.MaxValue, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Returns("token").Verifiable(Times.Once());

        // Act
        var result = await _page.OnPostAsync();

        // Assert
        Assert.That(result, Is.InstanceOf<PageResult>());
        _tokenServiceMock.Verify(s => s.Create(DefaultUsername, int.MaxValue, null), Times.Once);
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    [Test]
    public async Task OnPostAsync_DoesNotCallServices_WhenValidationFails()
    {
        // Arrange
        _page.ModelState.AddModelError("Username", "Invalid");

        // Act
        await _page.OnPostAsync();

        // Assert
        _tokenServiceMock.Verify(s => s.Create(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _linkStoreMock.Verify(s => s.CreateAsync(It.IsAny<SecretLinkClaims>()), Times.Never);
        _tokenServiceMock.Verify(s => s.TokenToJson(It.IsAny<SecretLinkClaims>()), Times.Never);
    }

    // ---------- OnPostAsync - Robustness ----------

    [Test]
    public void OnPostAsync_PropagatesTokenServiceCreateExceptions()
    {
        // Arrange
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null))
            .Throws(new InvalidOperationException("boom"))
            .Verifiable(Times.Once());

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => _page.OnPostAsync());
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
    }

    [Test]
    public void OnPostAsync_PropagatesLinkStoreExceptions()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).ThrowsAsync(new InvalidOperationException("boom")).Verifiable(Times.Once());

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => _page.OnPostAsync());
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    [Test]
    public void OnPostAsync_PropagatesTokenToJsonExceptions()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, null);
        _page.Input.Username = DefaultUsername;
        _page.Input.Max = 1;

        SetupValidatorSuccess(_page.Input, Times.Once());
        _tokenServiceMock.Setup(s => s.Create(DefaultUsername, 1, null)).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.CreateAsync(claims)).Returns(Task.CompletedTask).Verifiable(Times.Once());
        _tokenServiceMock.Setup(s => s.TokenToJson(claims)).Throws(new InvalidOperationException("boom")).Verifiable(Times.Once());

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => _page.OnPostAsync());
        _validatorMock.VerifyAll();
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();
    }

    // ---------- Helpers ----------

    private void SetupValidatorSuccess(CreateLinkViewModel input, Times times)
    {
        SetupValidator(input, new ValidationResult(), times);
    }

    private void SetupValidator(CreateLinkViewModel input, ValidationResult validationResult, Times times)
    {
        _validatorMock.Setup(v => v.ValidateAsync(
            It.Is<CreateLinkRequest>(r =>
                r.Username == input.Username &&
                r.Max == input.Max &&
                r.ExpiresAt == input.ExpiresAt),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(validationResult)
            .Verifiable(times);
    }

    private static IndexModel CreatePage(ITokenService tokenService,
                                         ILinkStore linkStore,
                                         IValidator<CreateLinkRequest> validator)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost");

        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        var pageContext = new PageContext(actionContext)
        {
            ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary(
                new EmptyModelMetadataProvider(),
                new ModelStateDictionary())
        };

        var page = new IndexModel(tokenService, linkStore, validator)
        {
            PageContext = pageContext
        };

        return page;
    }
}