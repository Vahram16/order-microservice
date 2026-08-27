using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.OrderPayments.Common;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Features.OrderPayments.GettingAction.V1;

internal sealed class GetOrderPaymentActionHandler(
    PaymentDbContext dbContext,
    IOrderPaymentProvider provider)
    : IQueryHandler<GetOrderPaymentActionQuery, Result<OrderPaymentActionResponse>>
{
    public async Task<Result<OrderPaymentActionResponse>> Handle(
        GetOrderPaymentActionQuery query,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.PaymentCustomers.AsNoTracking().SingleOrDefaultAsync(
            item => item.IdentityProvider == query.IdentityProvider && item.IdentitySubject == query.IdentitySubject,
            cancellationToken);
        if (customer is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var attempt = await dbContext.OrderPaymentAttempts.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == query.PaymentAttemptId && item.PaymentCustomerId == customer.Id,
            cancellationToken);
        if (attempt is null)
        {
            return PaymentApplicationErrors.PaymentAttemptNotFound;
        }

        if (attempt.Status != OrderPaymentStatus.RequiresCustomerAction || string.IsNullOrWhiteSpace(attempt.ProviderPaymentIntentId))
        {
            return PaymentApplicationErrors.PaymentActionNotRequired;
        }

        try
        {
            var session = await provider.GetAsync(attempt.ProviderPaymentIntentId, cancellationToken);
            if (!string.Equals(session.Status, "requires_action", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(session.ClientSecret))
            {
                return PaymentApplicationErrors.PaymentActionNotRequired;
            }

            return Result.Success(new OrderPaymentActionResponse(attempt.Id, "stripe", session.ClientSecret));
        }
        catch (PaymentProviderException)
        {
            return PaymentApplicationErrors.ProviderUnavailable;
        }
    }
}
