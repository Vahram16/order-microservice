using FluentValidation;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal sealed class UpdateCustomerDetailsValidator
    : AbstractValidator<UpdateCustomerDetailsCommand>
{
    public UpdateCustomerDetailsValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.FirstName).MaximumLength(100);
        RuleFor(command => command.LastName).MaximumLength(100);
        RuleFor(command => command.Email)
            .EmailAddress()
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.PhoneNumber).MaximumLength(32);
    }
}
