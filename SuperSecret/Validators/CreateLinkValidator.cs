using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.Extensions.Options;
using SuperSecret.Infrastructure;
using SuperSecret.Models;

namespace SuperSecret.Validators;

public partial class CreateLinkValidator : AbstractValidator<CreateLinkRequest>
{
    public CreateLinkValidator(IOptions<TokenOptions> options)
    {
        var maxExpiresInMinutes = options.Value.MaxTTLInMinutes;
        var maxClicks = options.Value.MaxClicks;

        RuleFor(x => x.Username)
            .NotEmpty().WithMessage(ValidationMessages.UsernameRequired)
            .Length(1, 50).WithMessage(ValidationMessages.UsernameLength)
            .Matches(UsernameRegex()).WithMessage(ValidationMessages.UsernameAlphanumeric);

        RuleFor(x => x.Max)
            .InclusiveBetween(1, maxClicks);

        RuleFor(x => x.ExpiresAt)
            .Cascade(CascadeMode.Stop)
            // If provided, must be in the future
            .Must(exp => !exp.HasValue || exp.Value > DateTimeOffset.UtcNow)
                .WithMessage(ValidationMessages.ExpiryDateFuture)
            // If provided, must be within the max TTL
            .Must(exp => !exp.HasValue || exp.Value <= DateTimeOffset.UtcNow.AddMinutes(maxExpiresInMinutes))
                .WithMessage(ValidationMessages.ExpiryDateMaxLimit);
    }

    [GeneratedRegex("^[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}