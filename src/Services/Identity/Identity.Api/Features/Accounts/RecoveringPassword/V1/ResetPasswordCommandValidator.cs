using FluentValidation;

namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

public sealed class ResetPasswordCommandValidator
    : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(4096);
        RuleFor(command => command.NewPassword)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(128);
    }
}
