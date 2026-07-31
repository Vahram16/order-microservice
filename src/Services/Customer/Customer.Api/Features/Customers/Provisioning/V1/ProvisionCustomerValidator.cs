using FluentValidation;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed class ProvisionCustomerValidator
    : AbstractValidator<ProvisionCustomerCommand>
{
    public ProvisionCustomerValidator()
    {
        RuleFor(command => command.Identity).NotNull();
    }
}
