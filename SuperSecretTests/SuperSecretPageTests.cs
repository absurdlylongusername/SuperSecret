using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Moq;
using NUlid;
using SuperSecret.Models;
using SuperSecret.Pages;
using SuperSecret.Services;

namespace SuperSecretTests;

[TestOf(nameof(SuperSecretModel))]
public class SuperSecretPageTests
{
    private const string DefaultUsername = "user";

    private readonly Mock<ITokenService> _tokenServiceMock = new(MockBehavior.Strict);
    private readonly Mock<ILinkStore> _linkStoreMock = new(MockBehavior.Strict);
    private SuperSecretModel _page;

    [SetUp]
    public void SetUp()
    {
        _page = CreatePage(_tokenServiceMock.Object, _linkStoreMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _tokenServiceMock.Reset();
        _linkStoreMock.Reset();
        _page = default!;
    }

    // ---------- General behavior ----------

    [Test]
    public async Task OnGetAsync_SetsNoStoreHeader_Always()
    {
        // Arrange
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(() => null).Verifiable();

        // Act
        await _page.OnGetAsync("anything");

        // Assert
        Assert.That(_page.HttpContext.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task OnGetAsync_ReturnsWhenTokenMissing_DoesNotCallTokenServiceOrLinkStore()
    {
        // Arrange
        // No setups needed as strict mocks will fail if called

        // Act
        await _page.OnGetAsync("");

        // Assert
        _tokenServiceMock.Verify(s => s.Validate(It.IsAny<string>()), Times.Never);
        _linkStoreMock.Verify(s => s.ConsumeSingleUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _linkStoreMock.Verify(s => s.ConsumeMultiUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        Assert.Multiple(() =>
        {
            Assert.That(_page.Success, Is.False);
            Assert.That(_page.Username, Is.EqualTo(string.Empty));
        });
    }

    // ---------- Token validation ----------

    [Test]
    public async Task OnGetAsync_InvalidToken_DoesNotCallLinkStore_LeavesDefaults()
    {
        // Arrange
        _tokenServiceMock.Setup(s => s.Validate("bad-token")).Returns((SecretLinkClaims?)null);

        // Act
        await _page.OnGetAsync("bad-token");

        // Assert
        _linkStoreMock.Verify(s => s.ConsumeSingleUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _linkStoreMock.Verify(s => s.ConsumeMultiUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        Assert.Multiple(() =>
        {
            Assert.That(_page.Success, Is.False);
            Assert.That(_page.Username, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public async Task OnGetAsync_ValidateCalled_WithExactTokenParameter()
    {
        // Arrange
        _tokenServiceMock.Setup(s => s.Validate("t0k3n")).Returns((SecretLinkClaims?)null).Verifiable(Times.Once());

        // Act
        await _page.OnGetAsync("t0k3n");

        // Assert
        _tokenServiceMock.VerifyAll();
    }

    // ---------- Single-use (Max null or 1) ----------

    [TestCase(null, null, TestName = "OnGetAsync_SingleUse_CallsConsumeSingleUse_WithNullMax_NullExp")]
    [TestCase(null, 5, TestName = "OnGetAsync_SingleUse_CallsConsumeSingleUse_WithNullMax_ExpProvided")]
    [TestCase(1, null, TestName = "OnGetAsync_SingleUse_CallsConsumeSingleUse_WithMax1_NullExp")]
    [TestCase(1, 5, TestName = "OnGetAsync_SingleUse_CallsConsumeSingleUse_WithMax1_ExpProvided")]
    public async Task OnGetAsync_SingleUse_CallsConsumeSingleUse_WithJtiAndExp(int? max, int? expMinutes)
    {
        // Arrange
        var jti = Ulid.NewUlid();
        DateTimeOffset? exp = expMinutes.HasValue ? DateTimeOffset.UtcNow.AddMinutes(expMinutes.Value) : null;
        var claims = new SecretLinkClaims(DefaultUsername, jti, max, exp);
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims).Verifiable(Times.Once());
        _linkStoreMock.Setup(s => s.ConsumeSingleUseAsync(jti, exp)).ReturnsAsync(true).Verifiable(Times.Once());

        // Act
        await _page.OnGetAsync("tok");

        // Assert
        _tokenServiceMock.VerifyAll();
        _linkStoreMock.VerifyAll();

        _linkStoreMock.Verify(s => s.ConsumeMultiUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
    }

    [Test]
    public async Task OnGetAsync_SingleUse_Success_SetsSuccessTrue_And_Username()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeSingleUseAsync(jti, claims.Exp)).ReturnsAsync(true);

        // Act
        await _page.OnGetAsync("tok");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(_page.Success, Is.True);
            Assert.That(_page.Username, Is.EqualTo(DefaultUsername));
        });
    }

    [Test]
    public async Task OnGetAsync_SingleUse_Failure_LeavesDefaults()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeSingleUseAsync(jti, claims.Exp)).ReturnsAsync(false);

        // Act
        await _page.OnGetAsync("tok");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(_page.Success, Is.False);
            Assert.That(_page.Username, Is.EqualTo(string.Empty));
        });
    }

    // ---------- Multi-use (Max > 1) ----------

    [Test]
    public async Task OnGetAsync_MultiUse_CallsConsumeMultiUse_WithJtiAndExp()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(5);
        var claims = new SecretLinkClaims(DefaultUsername, jti, 3, exp);
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeMultiUseAsync(jti, exp)).ReturnsAsync(2);

        // Act
        await _page.OnGetAsync("tok");

        // Assert
        _linkStoreMock.Verify(s => s.ConsumeMultiUseAsync(jti, exp), Times.Once);
        _linkStoreMock.Verify(s => s.ConsumeSingleUseAsync(It.IsAny<Ulid>(), It.IsAny<DateTimeOffset?>()), Times.Never);
    }

    [Test]
    public async Task OnGetAsync_MultiUse_RemainingPositive_SetsSuccessTrue_And_Username()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 5, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeMultiUseAsync(jti, claims.Exp)).ReturnsAsync(4);

        // Act
        await _page.OnGetAsync("tok");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(_page.Success, Is.True);
            Assert.That(_page.Username, Is.EqualTo(DefaultUsername));
        });
    }

    [Test]
    public async Task OnGetAsync_MultiUse_RemainingZero_SetsSuccessTrue_And_Username()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 2, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeMultiUseAsync(jti, claims.Exp)).ReturnsAsync(0);

        // Act
        await _page.OnGetAsync("tok");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(_page.Success, Is.True);
            Assert.That(_page.Username, Is.EqualTo(DefaultUsername));
        });
    }

    [Test]
    public async Task OnGetAsync_MultiUse_RemainingNull_SetsSuccessFalse_And_UsernameEmpty()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 2, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeMultiUseAsync(jti, claims.Exp)).ReturnsAsync((int?)null);

        // Act
        await _page.OnGetAsync("tok");

        Assert.Multiple(() =>
        {
            // Assert
            Assert.That(_page.Success, Is.False);
            Assert.That(_page.Username, Is.EqualTo(string.Empty));
        });
    }

    // ---------- Robustness ----------

    [Test]
    public void OnGetAsync_PropagatesStoreExceptions()
    {
        // Arrange
        var jti = Ulid.NewUlid();
        var claims = new SecretLinkClaims(DefaultUsername, jti, 1, DateTimeOffset.UtcNow.AddMinutes(5));
        _tokenServiceMock.Setup(s => s.Validate(It.IsAny<string>())).Returns(claims);
        _linkStoreMock.Setup(s => s.ConsumeSingleUseAsync(jti, claims.Exp)).ThrowsAsync(new InvalidOperationException("boom"));

        // Act + Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => _page.OnGetAsync("tok"));
    }

    // ---------- Helpers ----------

    private static SuperSecretModel CreatePage(ITokenService tokenService, ILinkStore linkStore)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        var pageContext = new PageContext(actionContext);

        var page = new SuperSecretModel(tokenService, linkStore)
        {
            PageContext = pageContext
        };

        return page;
    }
}