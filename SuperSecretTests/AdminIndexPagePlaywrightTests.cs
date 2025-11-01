using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using SuperSecret.Pages.Admin;
using SuperSecretTests.TestInfrastructure;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

    [SetUp]                // Per-test hook (shown in docs)
    public async Task GoToAdmin() => await Page.GotoAsync("/admin");

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

    // HTML input type="datetime-local" expects local time in "yyyy-MM-ddTHH:mm"
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

    [Test]
    public async Task Generates_Link_For_Valid_Input()
    {
        await base.Page.GetByLabel("Username").FillAsync("Alice");
        await base.Page.GetByRole(AriaRole.Spinbutton, new() { Name = "Max uses" }).FillAsync("1");  // adjust to your UI
        await base.Page.GetByRole(AriaRole.Button, new() { Name = "Generate" }).ClickAsync();

        // Example: assert the generated link textbox contains /supersecret/
        var linkBox = base.Page.GetByRole(AriaRole.Textbox, new() { Name = "Generated link" });
        await Expect(linkBox).ToBeVisibleAsync();
        await Expect(linkBox).ToHaveValueAsync(new Regex("/supersecret/"));
    }

    [Test]
    public async Task Following_Generated_Link_Shows_Secret()
    {
        // Generate
        await Page.GetByLabel("Username").FillAsync("Bob");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate" }).ClickAsync();
        var url = await Page.GetByRole(AriaRole.Textbox, new() { Name = "Generated link" }).InputValueAsync();

        // Visit the path; BaseURL lets us do relative or absolute
        await Page.GotoAsync(url);

        // Web-first assertion with auto-waiting
        await Expect(Page.GetByText("You have found the secret, Bob!")).ToBeVisibleAsync();
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {

    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {

    }
    // -------------- Happy Path - Single-use --------------

    private static IEnumerable<TestCaseData> ValidSingleUseTestCases()
    {
        yield return new TestCaseData("a", null);
        yield return new TestCaseData(new string('b', 50), 5);
        yield return new TestCaseData(DefaultUsername, 60);
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

    [TestCaseSource(nameof(ValidSingleUseTestCases))]
    public async Task UI_CreatesValidSingleUseLink_WithValidRequests(string username, int? expiresInMinutes)
    {
        // What should happen in this test?
        // Fills in the form with the data
        //Presses submit
        // Navigates to the Page, sees secret
        // Navigate again, sees no secret
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



        await Page.GetByRole(AriaRole.Heading, new() { Name = "You have found the secret," }).ClickAsync();
        await Page.GetByRole(AriaRole.Heading, new() { Name = "You have found the secret," }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync("You have found the secret, hello!");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You have found the secret," })).ToBeVisibleAsync();
        await Page.GotoAsync("http://localhost:5276/Admin");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();
        await Page.Locator("#generatedUrl").ClickAsync();
        await Page.Locator("#generatedUrl").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).ClickAsync();
        await Expect(Page.Locator("#generatedUrl")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Copy" })).ToBeVisibleAsync();
        await Expect(Page.Locator("#generatedUrl")).ToBeVisibleAsync();
        await Page.Locator("#generatedUrl").ClickAsync();
        await Page.Locator("#generatedUrl").ClickAsync();
        await Page.Locator("#generatedUrl").ClickAsync();
        await Page.Locator("#generatedUrl").ClickAsync(new LocatorClickOptions
        {
            Button = MouseButton.Right,
        });

    }

    [GeneratedRegex("/supersecret/")]
    private static partial Regex SuperSecretRegex();

    //private static IEnumerable<TestCaseData> SingleUseCases()
    //{
    //    yield return new TestCaseData((DateTimeOffset?)null).SetName("UI_CreateSingleUse_NoExpiry");
    //    yield return new TestCaseData(DateTimeOffset.UtcNow.AddMinutes(5)).SetName("UI_CreateSingleUse_WithFutureExpiry");
    //}

    //[TestCaseSource(nameof(SingleUseCases))]
    //public async Task UI_CreatesValidSingleUseLink_WithValidRequests(DateTimeOffset? expiry)
    //{
    //    await FillFormAsync(Username, 1, expiry);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    var resultInput = Page.Locator("#generatedUrl");
    //    await Expect(resultInput).ToBeVisibleAsync();
    //    var url = await resultInput.InputValueAsync();
    //    Assert.That(url, Does.StartWith("http"));

    //    // Works once
    //    await Page.GotoAsync(url);
    //    await Expect(Page.GetByText($"You have found the secret, {Username}!")).ToBeVisibleAsync();

    //    // Then fails
    //    await Page.GotoAsync(url);
    //    await Expect(Page.GetByText("There are no secrets here")).ToBeVisibleAsync();
    //}

    //// -------------- Happy Path - Multi-use --------------

    //private static IEnumerable<TestCaseData> MultiUseCases()
    //{
    //    yield return new TestCaseData((DateTimeOffset?)null).SetName("UI_CreateMultiUse_NoExpiry");
    //    yield return new TestCaseData(DateTimeOffset.UtcNow.AddMinutes(5)).SetName("UI_CreateMultiUse_WithFutureExpiry");
    //}

    //[TestCaseSource(nameof(MultiUseCases))]
    //public async Task UI_CreatesValidMultiUseLink_WithValidRequests(DateTimeOffset? expiry)
    //{
    //    const int maxClicks = 3;

    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, maxClicks, expiry);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    var resultInput = Page.Locator("#generatedUrl");
    //    await Expect(resultInput).ToBeVisibleAsync();
    //    var url = await resultInput.InputValueAsync();

    //    for (var i = 0; i < maxClicks; i++)
    //    {
    //        await Page.GotoAsync(url);
    //        await Expect(Page.GetByText($"You have found the secret, {Username}!")).ToBeVisibleAsync();
    //    }

    //    await Page.GotoAsync(url);
    //    await Expect(Page.GetByText("There are no secrets here")).ToBeVisibleAsync();
    //}

    //// -------------- Validation - Username --------------

    //private static IEnumerable<TestCaseData> InvalidUsernameCases()
    //{
    //    yield return new TestCaseData("", "Username is required").SetName("UI_Username_Empty_ShowsRequired");
    //    yield return new TestCaseData("   ", "Username is required").SetName("UI_Username_Whitespace_ShowsRequired");
    //    yield return new TestCaseData(new string('a', 51), "must be 1-50").SetName("UI_Username_TooLong_ShowsLength");
    //    yield return new TestCaseData("user test", "alphanumeric").SetName("UI_Username_NonAlphanumeric_ShowsError");
    //    yield return new TestCaseData("user@test", "alphanumeric").SetName("UI_Username_Symbols_ShowsError");
    //}

    //[TestCaseSource(nameof(InvalidUsernameCases))]
    //public async Task UI_ShowsValidation_ForInvalidUsername(string username, string expectedSnippet)
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(username, 1, null);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("body")).ToContainTextAsync(expectedSnippet);
    //}

    //// -------------- Validation - Max --------------

    //[Test]
    //public async Task UI_ShowsValidation_WhenMaxIsZero()
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, 0, null);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("body")).ToContainTextAsync("Max clicks must be at least 1");
    //}

    //[Test]
    //public async Task UI_ShowsValidation_WhenMaxIsNegative()
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, -5, null);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("body")).ToContainTextAsync("Max clicks must be at least 1");
    //}

    //// -------------- Validation - ExpiresAt --------------

    //[Test]
    //public async Task UI_ShowsValidation_WhenExpiresAtInPast()
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, 1, DateTimeOffset.UtcNow.AddMinutes(-5));
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("body")).ToContainTextAsync(new System.Text.RegularExpressions.Regex("future", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    //}

    //[Test]
    //public async Task UI_Succeeds_WhenExpiresAtInFuture()
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, 1, DateTimeOffset.UtcNow.AddMinutes(2));
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("#generatedUrl")).ToBeVisibleAsync();
    //    var url = await Page.Locator("#generatedUrl").InputValueAsync();
    //    Assert.That(url, Does.StartWith("http"));
    //}

    //[Test]
    //public async Task UI_Succeeds_WhenExpiresAtNull()
    //{
    //    await Page.GotoAsync("/Admin");
    //    await FillFormAsync(Username, 1, null);
    //    await Page.GetByRole(AriaRole.Button, new() { Name = "Generate Link" }).ClickAsync();

    //    await Expect(Page.Locator("#generatedUrl")).ToBeVisibleAsync();
    //    var url = await Page.Locator("#generatedUrl").InputValueAsync();
    //    Assert.That(url, Does.StartWith("http"));
    //}

    // -------------- Helpers --------------


}