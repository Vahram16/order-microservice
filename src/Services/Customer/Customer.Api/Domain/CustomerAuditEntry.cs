namespace Customer.Api.Domain;

public sealed class CustomerAuditEntry
{
    private CustomerAuditEntry()
    {
    }

    private CustomerAuditEntry(
        Guid id,
        Guid customerId,
        string actorSubject,
        string action,
        DateTimeOffset occurredAt,
        long customerVersion)
    {
        Id = id;
        CustomerId = customerId;
        ActorSubject = Required(actorSubject, nameof(actorSubject), 255);
        Action = Required(action, nameof(action), 64);
        OccurredAt = occurredAt;
        CustomerVersion = customerVersion;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string ActorSubject { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public long CustomerVersion { get; private set; }

    public static CustomerAuditEntry Create(
        Guid customerId,
        string actorSubject,
        string action,
        DateTimeOffset occurredAt,
        long customerVersion) =>
        new(Guid.NewGuid(), customerId, actorSubject, action, occurredAt, customerVersion);

    private static string Required(string value, string field, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new CustomerDomainException(
                "customer.audit_validation",
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}

internal static class CustomerAuditActions
{
    public const string Provisioned = "customer.provisioned";
    public const string DetailsUpdated = "customer.details_updated";
    public const string AddressAdded = "customer.address_added";
    public const string AddressUpdated = "customer.address_updated";
    public const string AddressRemoved = "customer.address_removed";
    public const string AccountClosed = "customer.account_closed";
}
