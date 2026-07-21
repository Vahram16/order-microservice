using FluentValidation;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed class AuthenticatorCodeCommandValidator
    : AbstractValidator<AuthenticatorCodeCommand>
{
    public AuthenticatorCodeCommandValidator() =>
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(32);
}
