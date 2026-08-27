namespace Order.Api.Persistence;

internal sealed class OrderCustomerProjection
{
    private OrderCustomerProjection() { }

    private OrderCustomerProjection(Guid customerId, string identityProvider, string identitySubject, DateTimeOffset updatedAt)
    {
        CustomerId = customerId;
        IdentityProvider = identityProvider;
        IdentitySubject = identitySubject;
        UpdatedAt = updatedAt;
    }

    public Guid CustomerId { get; private set; }
    public string IdentityProvider { get; private set; } = null!;
    public string IdentitySubject { get; private set; } = null!;
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OrderCustomerProjection Create(Guid customerId, string identityProvider, string identitySubject, DateTimeOffset now) =>
        new(customerId, identityProvider, identitySubject, now);

    public bool Matches(string identityProvider, string identitySubject) =>
        string.Equals(IdentityProvider, identityProvider, StringComparison.Ordinal) &&
        string.Equals(IdentitySubject, identitySubject, StringComparison.Ordinal);
}
