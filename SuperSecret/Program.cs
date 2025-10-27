using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<TokenOptions>(o =>
{
    o.SigningKey = builder.Configuration["TokenSigningKey"];
});

builder.Services.Configure<DatabaseOptions>(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// Services
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<ILinkStore, SqlLinkStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

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

    // Create claims and store
    var claims = tokenService.Create(req.Username, req.Max, req.ExpiresAt);
    await linkStore.CreateAsync(claims);

    // Generate URL
    var token = tokenService.TokenToJson(claims);
    var url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/supersecret/{token}";

    return Results.Ok(new CreateLinkResponse(url));
});

// Redirect root to admin
app.MapGet("/", () => Results.Redirect("/Admin"));

app.Run();



partial class Program
{
    [GeneratedRegex("^[A-Za-z0-9]+$")]
    private static partial Regex UsernameRegex();
}