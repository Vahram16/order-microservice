using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;

namespace Payment.Api.Persistence;

internal static class PaymentQueryExtensions
{
    public static Task<PaymentCustomer?> FindByIdentityAsync(
        this IQueryable<PaymentCustomer> customers,
        string identityProvider,
        string identitySubject,
        CancellationToken cancellationToken) =>
        customers.SingleOrDefaultAsync(
            customer =>
                customer.IdentityProvider == identityProvider &&
                customer.IdentitySubject == identitySubject,
            cancellationToken);
}
