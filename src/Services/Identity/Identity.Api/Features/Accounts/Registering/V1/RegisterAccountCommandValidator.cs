using FluentValidation;

namespace Identity.Api.Features.Accounts.Registering.V1;

public sealed class RegisterAccountCommandValidator
    : AbstractValidator<RegisterAccountCommand>
{
    public RegisterAccountCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
        RuleFor(command => command.Password)
            .NotEmpty()
            .MinimumLength(6)
            .MaximumLength(128);
        RuleFor(command => command.DisplayName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
