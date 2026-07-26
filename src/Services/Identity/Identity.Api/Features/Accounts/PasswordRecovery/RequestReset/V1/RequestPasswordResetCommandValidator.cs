using FluentValidation;

namespace Identity.Api.Features.Accounts.PasswordRecovery.RequestReset.V1;

public sealed class RequestPasswordResetCommandValidator
    : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
}
