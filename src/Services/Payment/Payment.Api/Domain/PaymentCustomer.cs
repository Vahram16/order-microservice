namespace Payment.Api.Domain;

public sealed class PaymentCustomer
{
    private PaymentCustomer()
    {
    }

    private PaymentCustomer(
        Guid customerId,
        string identityProvider,
        string identitySubject,
        DateTimeOffset now)
    {
        CustomerId = customerId;
        IdentityProvider = identityProvider;
        IdentitySubject = identitySubject;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid CustomerId { get; private set; }
    public string IdentityProvider { get; private set; } = string.Empty;
    public string IdentitySubject { get; private set; } = string.Empty;
    public string? StripeCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static PaymentCustomer Create(
        Guid customerId,
        string identityProvider,
        string identitySubject,
        DateTimeOffset now)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer id cannot be empty.", nameof(customerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(identityProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identitySubject);

        return new PaymentCustomer(
            customerId,
            identityProvider.Trim(),
            identitySubject.Trim(),
            now);
    }

    public void EnsureIdentity(string identityProvider, string identitySubject, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(identitySubject);

        if (!string.Equals(IdentityProvider, identityProvider.Trim(), StringComparison.Ordinal) ||
            !string.Equals(IdentitySubject, identitySubject.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A payment customer cannot be rebound to a different external identity.");
        }

        UpdatedAt = now;
    }

    public void AssignStripeCustomer(string stripeCustomerId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeCustomerId);
        var normalized = stripeCustomerId.Trim();

        if (StripeCustomerId is not null &&
            !string.Equals(StripeCustomerId, normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A payment customer cannot be rebound to a different Stripe customer.");
        }

        if (StripeCustomerId is null)
        {
            StripeCustomerId = normalized;
            Version++;
        }

        UpdatedAt = now;
    }
}
