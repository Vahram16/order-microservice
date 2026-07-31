using FluentValidation;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed class AddCustomerAddressValidator
    : AbstractValidator<AddCustomerAddressCommand>
{
    public AddCustomerAddressValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.AddressId).NotEmpty();
        RuleFor(command => command.Address).NotNull();
        // AddressData mirrors the flat request body; the command wrapper is not public.
        RuleFor(command => command.Address)
            .SetValidator(new AddCustomerAddressDataValidator())
            .OverridePropertyName(string.Empty);
    }
}
