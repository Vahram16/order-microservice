using FluentValidation;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
        RuleFor(command => command.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
