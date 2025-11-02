using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using SuperSecret.Pages.Admin;
using SuperSecret.Validators;
using SuperSecretTests.TestInfrastructure;
using System.Text.RegularExpressions;

namespace SuperSecretTests;

[TestOf(typeof(Program))]
[TestOf(typeof(IndexModel))]
[Category("Integration")]
[Parallelizable(ParallelScope.Self)]
public partial class AdminIndexPagePlaywrightTests : PageTest
{
    private const string DefaultUsername = "user";


    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = TestConfiguration.Options.BaseUrl,
        ViewportSize = new() { Width = 1280, Height = 800 },
        Permissions = ["clipboard-read", "clipboard-write"]
    };

    [SetUp]
    public async Task GoToAdmin() => await Page.GotoAsync("/admin");


    private static IEnumerable<TestCaseData> ValidSingleUseTestCases()
    {
        yield return new TestCaseData("a", null);
        yield return new TestCaseData(new string('b', 50), 5);
        yield return new TestCaseData(DefaultUsername, 60);
    }

    [TestCaseSource(nameof(ValidSingleUseTestCases))]
    public async Task UI_CreatesValidSingleUseLink_WithValidRequests(string username, int? expiresInMinutes)
    {
        var expiryDate = expiresInMinutes.HasValue
            ? DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes.Value)
            : (DateTimeOffset?)null;
        await FillFormAsync(username, 1, expiryDate);
        await SubmitFormAsync();

        var linkBox = Page.Locator("#generatedUrl");
        await Expect(linkBox).ToBeVisibleAsync();
        await Expect(linkBox).ToHaveValueAsync(SuperSecretRegex());

        var secretUrl = await CopyGeneratedUrlAsync();
        await Page.GotoAsync(secretUrl);

        await Expect(Page.GetByRole(AriaRole.Heading,
                                    new() { Name = $"You have found the secret, {username}" })).ToBeVisibleAsync();

        await Page.GotoAsync(secretUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "There are no secrets here" })).ToBeVisibleAsync();
    }

    [GeneratedRegex("/supersecret/")]
    private static partial Regex SuperSecretRegex();


    private static IEnumerable<TestCaseData> InvalidRequestTestCases()
    {
        yield return new TestCaseData("", 0, -10,
            ValidationMessages.UsernameRequired, 
            ValidationMessages.MaxClicksMinimum, 
            ValidationMessages.ExpiryDateFuture);
        yield return new TestCaseData(" ", -1, 0,
            ValidationMessages.UsernameLengthAlphanumeric, 
            ValidationMessages.MaxClicksMinimum, 
            ValidationMessages.ExpiryDateFuture);
        yield return new TestCaseData("nice@username", 2, null,
            ValidationMessages.UsernameAlphanumeric, 
            null, 
            null);
        yield return new TestCaseData("with space", 2, null,
            ValidationMessages.UsernameAlphanumeric, 
            null, 
            null);
        yield return new TestCaseData(new string('c', 51), 2, null,
            ValidationMessages.UsernameLength, 
            null, 
            null);
    }

    // TODO: Fix validation tests
    [TestCaseSource(nameof(InvalidRequestTestCases))]

    public async Task UI_ShowsValidationErrors_ForInvalidRequests(string username,
                                                                  int maxClicks,
                                                                  int? expiresInMinutes,
                                                                  string? usernameValidationMessage,
                                                                  string? maxClicksValidationMessage,
                                                                  string? expiryDateValidationMessage)
    {
        var expiryDate = expiresInMinutes.HasValue
            ? DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes.Value)
            : (DateTimeOffset?)null;
        await FillFormAsync(username, maxClicks, expiryDate);
        await SubmitFormAsync();

        var linkBox = Page.Locator("#generatedUrl");
        await Expect(linkBox).Not.ToBeVisibleAsync();

        var copyButton = Page.GetByRole(AriaRole.Button, new() { Name = "Copy" });
        await Expect(copyButton).Not.ToBeVisibleAsync();

        foreach (var validationMessage in (string?[])[usernameValidationMessage,
                                                      maxClicksValidationMessage, 
                                                      expiryDateValidationMessage])
        {
            if (validationMessage != null)
            {
                await Expect(Page.GetByText(validationMessage)).ToBeVisibleAsync();
            }
        }
    }

    // -------------- Helpers --------------

    private async Task FillUsername(string? username)
    {
        if (username == null) return;

        var usernameInput = Page.GetByRole(AriaRole.Textbox, new() { Name = "Username" });
        await usernameInput.ClickAsync();
        await usernameInput.FillAsync(username);
    }

    private async Task FillMaxClicks(int maxClicks)
    {
        var maxClicksInput = Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Max Clicks" });
        await maxClicksInput.ClickAsync();
        await maxClicksInput.FillAsync(maxClicks.ToString());
    }

    private static string ToDatetimeLocal(DateTimeOffset dto)
        => dto.ToLocalTime().ToString("yyyy-MM-ddTHH:mm");

    private async Task FillExpiryDate(DateTimeOffset? expiresAt)
    {
        if (expiresAt == null) return;

        var expiresAtInput = Page.GetByRole(AriaRole.Textbox, new() { Name = "Expires At" });
        await expiresAtInput.ClickAsync();
        await expiresAtInput.FillAsync(ToDatetimeLocal(expiresAt.Value));
    }

    private async Task FillFormAsync(string? username, int maxClicks, DateTimeOffset? expiresAt = null)
    {
        await FillUsername(username);
        await FillMaxClicks(maxClicks);
        await FillExpiryDate(expiresAt);
    }


    private async Task SubmitFormAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();
    }

    private async Task<string> GetGeneratedUrlAsync()
    {
        return await Page.Locator("#generatedUrl").InputValueAsync();
    }

    private async Task<string> CopyGeneratedUrlAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).ClickAsync();
        await Expect(Page.GetByText("Link copied!")).ToBeVisibleAsync();
        return await Page.EvaluateAsync<string>("navigator.clipboard.readText()");
    }
}