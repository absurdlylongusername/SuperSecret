using FluentValidation;
using SuperSecret.Infrastructure;
using SuperSecret.Models;
using SuperSecret.Services;
using SuperSecret.Validators;

var builder = WebApplication.CreateBuilder(args);

// Options
builder.Services.Configure<TokenOptions>(builder.Configuration.GetRequiredSection(nameof(TokenOptions)));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetRequiredSection(nameof(DatabaseOptions)));

// Infrastructure
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// Services
builder.Services.AddRazorPages();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<ILinkStore, SqlLinkStore>();
builder.Services.AddScoped<IValidator<CreateLinkRequest>, CreateLinkValidator>();

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

    var claims = tokenService.Create(request.Username, request.Max, request.ExpiresAt);
    await linkStore.CreateAsync(claims);

    var token = tokenService.TokenToJson(claims);
    var url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/supersecret/{token}";

    return Results.Ok(new CreateLinkResponse(url));
});

// Redirect root to admin
app.MapGet("/", () => Results.Redirect("/Admin"));

app.Run();



public partial class Program
{
}