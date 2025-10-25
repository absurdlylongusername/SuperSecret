using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SuperSecret.Services;

namespace SuperSecret.Pages;

public class SuperSecretModel : PageModel
{
    private readonly ITokenService _tokenService;
    private readonly ILinkStore _linkStore;

    public bool Success { get; private set; }
  public string Username { get; private set; } = "";

    public SuperSecretModel(ITokenService tokenService, ILinkStore linkStore)
 {
        _tokenService = tokenService;
   _linkStore = linkStore;
    }

    public async Task OnGetAsync(string token)
    {
    // Add no-cache headers
        Response.Headers["Cache-Control"] = "no-store";

        if (string.IsNullOrWhiteSpace(token))
   return;

      var claims = _tokenService.Validate(token);
        if (claims is null)
     return;

  var maxClicks = claims.Max ?? 1;
    if (maxClicks == 1)
        {
    Success = await _linkStore.ConsumeSingleUseAsync(claims.Jti, claims.Exp);
  }
        else
     {
            var remaining = await _linkStore.ConsumeMultiUseAsync(claims.Jti, claims.Exp);
Success = remaining.HasValue; // null = deny; 0 or >0 = allowed
        }

        if (Success)
   Username = claims.Sub;
    }
}
