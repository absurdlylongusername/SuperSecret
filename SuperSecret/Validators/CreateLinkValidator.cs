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
            .Must(date => date!.Value > DateTimeOffset.UtcNow && date.Value <= DateTimeOffset.UtcNow.AddMinutes(maxExpiresInMinutes))
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage(ValidationMessages.ExpiryDateFuture);
    }

    [GeneratedRegex("^[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();
}