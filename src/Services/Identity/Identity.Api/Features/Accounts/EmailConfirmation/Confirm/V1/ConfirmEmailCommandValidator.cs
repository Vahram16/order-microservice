using FluentValidation;

namespace Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1;

public sealed class ConfirmEmailCommandValidator
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
