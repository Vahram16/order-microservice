using Payment.Api.Domain;

namespace Payment.Api.Tests;

public sealed class PaymentCustomerTests
{
    [Fact]
    public void StripeCustomerAssignmentIsIdempotentButCannotRebind()
    {
        var customer = PaymentCustomer.Create(
            Guid.NewGuid(),
            "keycloak",
            "subject-1",
            DateTimeOffset.UtcNow);

        customer.AssignStripeCustomer("cus_123", DateTimeOffset.UtcNow);
        customer.AssignStripeCustomer("cus_123", DateTimeOffset.UtcNow);

        Assert.Equal("cus_123", customer.StripeCustomerId);
        Assert.Throws<InvalidOperationException>(() =>
            customer.AssignStripeCustomer("cus_other", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IdentityCannotBeReboundToAnotherSubject()
    {
        var customer = PaymentCustomer.Create(
            Guid.NewGuid(),
            "keycloak",
            "subject-1",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            customer.EnsureIdentity("keycloak", "subject-2", DateTimeOffset.UtcNow));
    }
}
