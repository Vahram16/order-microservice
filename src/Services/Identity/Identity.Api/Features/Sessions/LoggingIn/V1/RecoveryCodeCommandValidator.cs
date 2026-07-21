using FluentValidation;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

public sealed class RecoveryCodeCommandValidator
    : AbstractValidator<RecoveryCodeCommand>
{
    public RecoveryCodeCommandValidator() =>
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(128);
}
