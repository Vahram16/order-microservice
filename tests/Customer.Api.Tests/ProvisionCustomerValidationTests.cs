using Customer.Api.Features.Customers.Provisioning.V1;

namespace Customer.Api.Tests;

public sealed class ProvisionCustomerValidationTests
{
    [Fact]
    public void ValidatorHandlesNullIdentityWithoutDereferencingIt()
    {
        var validator = new ProvisionCustomerValidator();

        var result = validator.Validate(new ProvisionCustomerCommand(null!));

        var failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(ProvisionCustomerCommand.Identity), failure.PropertyName);
    }
}
