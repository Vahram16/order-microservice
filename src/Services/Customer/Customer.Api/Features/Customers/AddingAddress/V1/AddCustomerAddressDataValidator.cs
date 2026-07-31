using Customer.Api.Domain;
using FluentValidation;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal sealed class AddCustomerAddressDataValidator : AbstractValidator<AddressData>
{
    public AddCustomerAddressDataValidator()
    {
        RuleFor(address => address.Label).MaximumLength(50);
        RuleFor(address => address.RecipientName).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line1).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line2).MaximumLength(200);
        RuleFor(address => address.City).NotEmpty().MaximumLength(100);
        RuleFor(address => address.Region).MaximumLength(100);
        RuleFor(address => address.PostalCode).NotEmpty().MaximumLength(32);
        RuleFor(address => address.CountryCode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Length(2)
            .Matches("^[A-Za-z]{2}$")
            .WithMessage("CountryCode must be an ISO 3166-1 alpha-2 code.");
        RuleFor(address => address.PhoneNumber).MaximumLength(32);
    }
}
