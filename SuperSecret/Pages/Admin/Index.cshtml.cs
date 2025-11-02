using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;
using System.Diagnostics;

namespace SuperSecret.Pages.Admin;

public class IndexModel(ITokenService tokenService,
                        ILinkStore linkStore,
                        IValidator<CreateLinkRequest> validator,
                        IOptions<TokenOptions> tokenOptions) : PageModel
{
    [BindProperty]
    public CreateLinkViewModel Input { get; set; } = new();

    public string? GeneratedUrl { get; set; }

    public int MaxClicks { get; } = tokenOptions.Value.MaxClicks;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var request = new CreateLinkRequest(Input.Username, Input.Max, Input.ExpiresAt);

        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }
            return Page();
        }

        var claims = tokenService.Create(Input.Username, Input.Max, Input.ExpiresAt);
        await linkStore.CreateAsync(claims);

        var token = tokenService.TokenToJson(claims);
        GeneratedUrl = $"{Request.Scheme}://{Request.Host}/supersecret/{token}";

        return Page();
    }
}
