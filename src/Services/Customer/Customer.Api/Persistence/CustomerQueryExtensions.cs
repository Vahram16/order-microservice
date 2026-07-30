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
}
