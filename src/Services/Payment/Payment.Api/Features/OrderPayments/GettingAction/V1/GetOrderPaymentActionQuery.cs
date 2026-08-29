using Microservices.Application;

namespace Payment.Api.Features.OrderPayments.GettingAction.V1;

internal sealed record GetOrderPaymentActionQuery(
    Guid PaymentAttemptId,
    string IdentityProvider,
    string IdentitySubject)
    : IQuery<Result<OrderPaymentActionResponse>>;
