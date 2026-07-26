using FluentValidation;

namespace Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1;

public sealed class RecoveryCodeCommandValidator
    : AbstractValidator<RecoveryCodeCommand>
{
    public RecoveryCodeCommandValidator() =>
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(128);
}
