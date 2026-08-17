using Microservices.Application;

namespace Payment.Api.Features.PaymentMethods.Listing.V1;

internal sealed record ListPaymentMethodsQuery(
    string IdentityProvider,
    string IdentitySubject) : IQuery<Result<IReadOnlyList<PaymentMethodResponse>>>;
