using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Persistence;

internal static class CustomerAddressPersistence
{
    internal static async Task ClearCompetingAddressDefaultsAsync(
        this CustomerDbContext dbContext,
        Guid customerId,
        Guid targetAddressId,
        bool isDefaultShipping,
        bool isDefaultBilling,
        CancellationToken cancellationToken)
    {
        if (!isDefaultShipping && !isDefaultBilling)
        {
            return;
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Competing address defaults must be cleared inside the aggregate save transaction.");
        }

        // These updates run before SaveChanges so PostgreSQL's filtered unique indexes
        // never observe the old and new default rows at the same time. ExecuteUpdate
        // bypasses tracking; the aggregate mutation that follows mirrors these changes
        // on the already-loaded addresses.
        if (isDefaultShipping)
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

        if (isDefaultBilling)
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
}
