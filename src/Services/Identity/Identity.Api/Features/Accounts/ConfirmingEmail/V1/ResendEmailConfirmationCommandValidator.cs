using FluentValidation;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed class ResendEmailConfirmationCommandValidator
    : AbstractValidator<ResendEmailConfirmationCommand>
{
    public ResendEmailConfirmationCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
}
