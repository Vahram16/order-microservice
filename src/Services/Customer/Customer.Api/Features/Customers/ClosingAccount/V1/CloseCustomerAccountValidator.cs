using FluentValidation;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal sealed class CloseCustomerAccountValidator
    : AbstractValidator<CloseCustomerAccountCommand>
{
    public CloseCustomerAccountValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}
