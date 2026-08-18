using Payment.Api.Domain;

namespace Payment.Api.Tests;

public sealed class PaymentDomainTests
{
    [Fact]
    public void PaymentCustomerUsesAuthoritativeCustomerIdentityAndProviderLinkIsIdempotent()
    {
        var id = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var creation = PaymentCustomer.Create(
            id,
            customerId,
            "keycloak",
            "subject-123",
            now);

        Assert.True(creation.IsSuccess);
        var customer = creation.Value;
        Assert.Equal(customerId, customer.CustomerId);
        Assert.True(customer.AssignProviderCustomer("cus_one", now).IsSuccess);
        Assert.True(customer.AssignProviderCustomer("cus_one", now).IsSuccess);

        var conflict = customer.AssignProviderCustomer("cus_two", now);
        Assert.True(conflict.IsFailure);
        Assert.Equal("payment.provider_customer_conflict", conflict.Error.Code);
        Assert.Equal("cus_one", customer.ProviderCustomerId);
    }

    [Fact]
    public void PaymentCustomerCannotBeReboundToAnotherCustomerIdentity()
    {
        var creation = PaymentCustomer.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "keycloak",
            "subject-123",
            DateTimeOffset.UtcNow);
        Assert.True(creation.IsSuccess);

        var conflict = creation.Value.EnsureCustomerIdentity(
            Guid.NewGuid(),
            "keycloak",
            "subject-123");

        Assert.True(conflict.IsFailure);
        Assert.Equal("payment.customer_identity_conflict", conflict.Error.Code);
    }

    [Fact]
    public void PaymentMethodStoresOnlyReusableDisplayMetadata()
    {
        var creation = PaymentMethod.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pm_123",
            new CardPaymentMethodDetails("visa", "4242", 12, 2032, "apple_pay"),
            true,
            DateTimeOffset.UtcNow);

        Assert.True(creation.IsSuccess);
        Assert.Equal("visa", creation.Value.Brand);
        Assert.Equal("4242", creation.Value.Last4);
        Assert.Equal("apple_pay", creation.Value.WalletType);
        Assert.True(creation.Value.IsDefault);
    }

    [Fact]
    public void PaymentMethodRejectsPanShapedLastFourWithoutMutatingState()
    {
        var creation = PaymentMethod.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "pm_123",
            new CardPaymentMethodDetails("visa", "4242424242424242", 12, 2032, null),
            false,
            DateTimeOffset.UtcNow);

        Assert.True(creation.IsFailure);
        Assert.Equal("payment.validation", creation.Error.Code);
    }
}
