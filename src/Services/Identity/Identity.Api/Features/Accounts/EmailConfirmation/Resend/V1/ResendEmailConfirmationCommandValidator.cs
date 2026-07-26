using FluentValidation;

namespace Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1;

public sealed class ResendEmailConfirmationCommandValidator
    : AbstractValidator<ResendEmailConfirmationCommand>
{
    public ResendEmailConfirmationCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
}
