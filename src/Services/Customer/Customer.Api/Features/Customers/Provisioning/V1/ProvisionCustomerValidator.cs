using FluentValidation;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed class ProvisionCustomerValidator
    : AbstractValidator<ProvisionCustomerCommand>
{
    public ProvisionCustomerValidator()
    {
        RuleFor(command => command.Identity).NotNull();
        RuleFor(command => command.Identity.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Identity.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.Identity.GivenName).MaximumLength(100);
        RuleFor(command => command.Identity.FamilyName).MaximumLength(100);
        RuleFor(command => command.Identity.Email)
            .EmailAddress()
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Identity.Email));
    }
}
