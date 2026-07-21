using FluentValidation;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed class ConfirmEmailCommandValidator
    : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
