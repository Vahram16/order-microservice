using FluentValidation;

namespace Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;

public sealed class AuthenticatorCodeCommandValidator
    : AbstractValidator<AuthenticatorCodeCommand>
{
    public AuthenticatorCodeCommandValidator() =>
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(32);
}
