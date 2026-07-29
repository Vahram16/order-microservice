using Customer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Customer.Api.Persistence;

internal static class CustomerPersistence
{
    public static Task<Domain.Customer?> FindAsync(
        CustomerDbContext dbContext,
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        dbContext.Customers
            .Include(customer => customer.Addresses)
            .SingleOrDefaultAsync(
                customer =>
                    customer.IdentityProvider == provider &&
                    customer.IdentitySubject == subject,
                cancellationToken);

    public static async Task<Domain.Customer> LoadRequiredAsync(
        CustomerDbContext dbContext,
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        await FindAsync(dbContext, provider, subject, cancellationToken)
        ?? throw new CustomerNotFoundException();

    public static void AddAudit(
        CustomerDbContext dbContext,
        Domain.Customer customer,
        string actorSubject,
        string action,
        DateTimeOffset occurredAt) =>
        dbContext.CustomerAuditEntries.Add(CustomerAuditEntry.Create(
            customer.Id,
            actorSubject,
            action,
            occurredAt,
            customer.Version));

    public static async Task ClearCompetingDefaultsAsync(
        CustomerDbContext dbContext,
        Guid customerId,
        Guid targetAddressId,
        AddressData address,
        CancellationToken cancellationToken)
    {
        if (address.IsDefaultShipping)
        {
            await dbContext.CustomerAddresses
                .Where(candidate =>
                    candidate.CustomerId == customerId &&
                    candidate.Id != targetAddressId &&
                    candidate.IsDefaultShipping)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.IsDefaultShipping,
                        false),
                    cancellationToken);
        }

        if (address.IsDefaultBilling)
        {
            await dbContext.CustomerAddresses
                .Where(candidate =>
                    candidate.CustomerId == customerId &&
                    candidate.Id != targetAddressId &&
                    candidate.IsDefaultBilling)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.IsDefaultBilling,
                        false),
                    cancellationToken);
        }
    }

    public static bool IsUniqueConstraintViolation(
        DbUpdateException exception,
        string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: var actualConstraint
        } && string.Equals(actualConstraint, constraintName, StringComparison.Ordinal);
}

internal static class CustomerConstraintNames
{
    public const string Identity = "IX_customers_IdentityProvider_IdentitySubject";
    public const string AddressPrimaryKey = "PK_customer_addresses";
    public const string DefaultShipping = "UX_customer_addresses_default_shipping";
    public const string DefaultBilling = "UX_customer_addresses_default_billing";
}
