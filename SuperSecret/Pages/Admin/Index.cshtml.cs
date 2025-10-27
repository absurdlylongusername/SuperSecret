using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SuperSecret.Models;
using SuperSecret.Services;

namespace SuperSecret.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly ITokenService _tokenService;
    private readonly ILinkStore _linkStore;

    [BindProperty]
    public CreateLinkViewModel Input { get; set; } = new();

    public string? GeneratedUrl { get; set; }

    public IndexModel(ITokenService tokenService, ILinkStore linkStore)
    {
        _tokenService = tokenService;
        _linkStore = linkStore;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Validate expiry is in future if provided
        if (Input.ExpiresAt.HasValue && Input.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(nameof(Input.ExpiresAt), "Expiry date must be in the future.");
            return Page();
        }

        // Create claims and store
        var claims = _tokenService.Create(Input.Username, Input.Max, Input.ExpiresAt);
        await _linkStore.CreateAsync(claims);

        // Generate URL
        var token = _tokenService.TokenToJson(claims);
        GeneratedUrl = $"{Request.Scheme}://{Request.Host}/supersecret/{token}";

        return Page();
    }
}
