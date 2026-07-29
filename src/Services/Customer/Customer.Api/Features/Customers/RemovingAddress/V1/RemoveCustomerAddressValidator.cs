using FluentValidation;

namespace Customer.Api.Features.Customers.RemovingAddress.V1;

internal sealed class RemoveCustomerAddressValidator
    : AbstractValidator<RemoveCustomerAddressCommand>
{
    public RemoveCustomerAddressValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.AddressId).NotEmpty();
    }
}
