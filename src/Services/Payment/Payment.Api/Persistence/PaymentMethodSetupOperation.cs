namespace Payment.Api.Persistence;

internal sealed class PaymentMethodSetupOperation
{
    private PaymentMethodSetupOperation() { }

    public Guid Id { get; private set; }
    public Guid PaymentCustomerId { get; private set; }
    public string? ProviderSetupIntentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PaymentMethodSetupOperation Create(
        Guid id,
        Guid paymentCustomerId,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            PaymentCustomerId = paymentCustomerId,
            CreatedAt = now,
            UpdatedAt = now
        };

    public bool TryAssignProviderSetupIntent(string providerSetupIntentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSetupIntentId);

        if (ProviderSetupIntentId is not null)
        {
            return string.Equals(
                ProviderSetupIntentId,
                providerSetupIntentId,
                StringComparison.Ordinal);
        }

        ProviderSetupIntentId = providerSetupIntentId;
        UpdatedAt = now;
        return true;
    }
}
