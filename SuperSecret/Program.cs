using System.Text.RegularExpressions;
using SuperSecret.Models;
using SuperSecret.Services;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<ILinkStore, SqlLinkStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// API Endpoints
var api = app.MapGroup("/api");

api.MapPost("/links", async (CreateLinkRequest req, ITokenService tokenService, ILinkStore linkStore, HttpContext ctx) =>
{
    // Validate username
    if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length > 50 || !UsernameRegex().IsMatch(req.Username))
  {
        return Results.BadRequest("Username must be 1-50 alphanumeric characters only.");
    }

    // Validate max clicks
    if (req.Max < 1)
    {
        return Results.BadRequest("Max clicks must be at least 1.");
    }

    // Validate expiry date (if provided, must be in the future)
    if (req.ExpiresAt.GetValueOrDefault() <= DateTimeOffset.UtcNow)
    {
        return Results.BadRequest("Expiry date must be in the future.");
    }

    // Create claims and linkStore
    var claims = tokenService.Create(req.Username, req.Max, req.ExpiresAt);
    await linkStore.CreateAsync(claims);

    // Generate URL
    var token = tokenService.Pack(claims);
    var url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/supersecret/{token}";

    return Results.Ok(new CreateLinkResponse(url));
});

app.Run();



partial class Program
{
    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex UsernameRegex();
}