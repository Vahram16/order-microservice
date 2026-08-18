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
        var customer = await dbContext.PaymentCustomers
            .AsNoTracking()
            .FindByIdentityAsync(
                query.Identity.Provider,
                query.Identity.Subject,
                cancellationToken);

        if (customer is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var methods = await dbContext.PaymentMethods
            .AsNoTracking()
            .Where(method =>
                method.PaymentCustomerId == customer.Id &&
                method.Status == PaymentMethodStatus.Active)
            .OrderByDescending(method => method.IsDefault)
            .ThenBy(method => method.CreatedAt)
            .Select(method => new PaymentMethodResponse(
                method.Id,
                method.Brand,
                method.Last4,
                method.ExpMonth,
                method.ExpYear,
                method.WalletType,
                method.IsDefault,
                method.Status.ToString()))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PaymentMethodResponse>>(methods);
    }
}
