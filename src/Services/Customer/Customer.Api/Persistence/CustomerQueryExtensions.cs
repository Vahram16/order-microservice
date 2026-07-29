using Customer.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Persistence;

internal static class CustomerQueryExtensions
{
    internal static Task<Domain.Customer?> FindByIdentityAsync(
        this IQueryable<Domain.Customer> customers,
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        customers
            .Include(customer => customer.Addresses)
            .SingleOrDefaultAsync(
                customer =>
                    customer.IdentityProvider == provider &&
                    customer.IdentitySubject == subject,
                cancellationToken);

    internal static async Task<Domain.Customer> GetRequiredByIdentityAsync(
        this IQueryable<Domain.Customer> customers,
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        await customers.FindByIdentityAsync(
            provider,
            subject,
            cancellationToken)
        ?? throw new CustomerNotFoundException();
}
