using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Features.PaymentMethods.Listing.V1;

internal sealed class ListPaymentMethodsHandler(PaymentDbContext dbContext)
    : IQueryHandler<ListPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodResponse>>>
{
    public async Task<Result<IReadOnlyList<PaymentMethodResponse>>> Handle(
        ListPaymentMethodsQuery query,
        CancellationToken cancellationToken)
    {
        var customerId = await dbContext.PaymentCustomers
            .Where(customer => customer.IdentityProvider == query.IdentityProvider &&
                               customer.IdentitySubject == query.IdentitySubject)
            .Select(customer => (Guid?)customer.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (customerId is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var methods = await dbContext.PaymentMethods
            .AsNoTracking()
            .Where(method => method.CustomerId == customerId.Value &&
                             method.Status == SavedPaymentMethodStatus.Active)
            .OrderByDescending(method => method.IsDefault)
            .ThenByDescending(method => method.CreatedAt)
            .Select(method => new PaymentMethodResponse(
                method.Id,
                method.Type,
                method.Brand,
                method.Last4,
                method.ExpMonth,
                method.ExpYear,
                method.WalletType,
                method.IsDefault))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PaymentMethodResponse>>(methods);
    }
}
