using Customer.Api.Domain;

namespace Customer.Api.Persistence;

internal static class CustomerAuditExtensions
{
    internal static void AddAuditEntry(
        this CustomerDbContext dbContext,
        Domain.Customer customer,
        string actorSubject,
        string action,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(customer);

        dbContext.CustomerAuditEntries.Add(CustomerAuditEntry.Create(
            customer.Id,
            actorSubject,
            action,
            occurredAt,
            customer.Version));
    }
}
