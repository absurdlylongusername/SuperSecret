using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SuperSecret.Models;
using SuperSecret.Services;

namespace SuperSecret.Pages.Admin;

public class IndexModel(ITokenService tokenService,
                        ILinkStore linkStore,
                        IValidator<CreateLinkRequest> validator) : PageModel
{
    [BindProperty]
    public CreateLinkViewModel Input { get; set; } = new();

    public string? GeneratedUrl { get; set; }

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
            // IMPORTANT: prefix property name with "Input." so Razor binds errors to the correct fields
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }
            return Page();
        }

        // Create claims and store
        var claims = tokenService.Create(Input.Username, Input.Max, Input.ExpiresAt);
        await linkStore.CreateAsync(claims);

        // Generate URL
        var token = tokenService.TokenToJson(claims);
        GeneratedUrl = $"{Request.Scheme}://{Request.Host}/supersecret/{token}";

        return Page();
    }
}
