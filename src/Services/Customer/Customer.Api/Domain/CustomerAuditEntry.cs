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
        ActorSubject = actorSubject;
        Action = action;
        OccurredAt = occurredAt;
        CustomerVersion = customerVersion;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string ActorSubject { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public long CustomerVersion { get; private set; }

    internal static CustomerAuditEntry Create(
        Guid customerId,
        string actorSubject,
        string action,
        DateTimeOffset occurredAt,
        long customerVersion)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer identifier cannot be empty.", nameof(customerId));
        }

        var normalizedActor = Required(actorSubject, nameof(actorSubject), 255);
        var normalizedAction = Required(action, nameof(action), 64);

        if (customerVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(customerVersion),
                customerVersion,
                "Customer version must be positive.");
        }

        return new CustomerAuditEntry(
            Guid.NewGuid(),
            customerId,
            normalizedActor,
            normalizedAction,
            occurredAt,
            customerVersion);
    }

    private static string Required(string? value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
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
