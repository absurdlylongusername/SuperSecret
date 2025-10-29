using System.Text.RegularExpressions;
using FluentValidation;
using SuperSecret.Models;

namespace SuperSecret.Validators;

public class CreateLinkValidator : AbstractValidator<CreateLinkRequest>
{
    private static readonly Regex UsernameRegex = new("^[A-Za-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CreateLinkValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage(ValidationMessages.UsernameRequired)
            .Length(1, 50).WithMessage(ValidationMessages.UsernameLength)
            .Matches(UsernameRegex).WithMessage(ValidationMessages.UsernameAlphanumeric);

        RuleFor(x => x.Max)
            .GreaterThanOrEqualTo(1).WithMessage(ValidationMessages.MaxClicksMinimum);

        RuleFor(x => x.ExpiresAt)
            .Must(date => !date.HasValue || date.Value > DateTimeOffset.UtcNow)
            .When(x => x.ExpiresAt.HasValue)
            .WithMessage(ValidationMessages.ExpiryDateFuture);
    }
}