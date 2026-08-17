using Microservices.Application;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;
namespace Payment.Api.Features.PaymentMethods.Listing.V1;

internal sealed record ListPaymentMethodsQuery(CurrentPaymentIdentity Identity) : IQuery<Result<IReadOnlyList<PaymentMethodResponse>>>;
