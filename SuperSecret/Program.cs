using FluentValidation;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;
using SuperSecret.Validators;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<TokenOptions>(o =>
{
    o.TokenSigningKey = builder.Configuration[nameof(TokenOptions.TokenSigningKey)];
});

builder.Services.Configure<DatabaseOptions>(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

builder.Services.Configure<CleanupOptions>(builder.Configuration.GetSection(nameof(CleanupOptions)));

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// Services
builder.Services.AddRazorPages();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<ILinkStore, SqlLinkStore>();
builder.Services.AddSingleton<IValidator<CreateLinkRequest>, CreateLinkValidator>();
builder.Services.AddSingleton<ILinkCleanupService, LinkCleanupService>();

// Background Services
builder.Services.AddHostedService<ExpiredLinkCleanupService>();

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

api.MapPost("/links", async (CreateLinkRequest request,
                             ITokenService tokenService,
                             ILinkStore linkStore,
                             IValidator<CreateLinkRequest> validator,
                             HttpContext ctx) =>
{
    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        return Results.ValidationProblem(validationResult.ToDictionary());
    }

    // Create claims and store
    var claims = tokenService.Create(request.Username, request.Max, request.ExpiresAt);
    await linkStore.CreateAsync(claims);

    // Generate URL
    var token = tokenService.TokenToJson(claims);
    var url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/supersecret/{token}";

    return Results.Ok(new CreateLinkResponse(url));
});

app.MapOpenApi();
app.MapScalarApiReference();

// Redirect root to admin
app.MapGet("/", () => Results.Redirect("/Admin"));

app.Run();



public partial class Program
{ }