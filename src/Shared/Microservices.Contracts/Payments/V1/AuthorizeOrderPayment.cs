using Microservices.Contracts;

namespace Microservices.Contracts.Payments.V1;

public sealed record AuthorizeOrderPayment(
    Guid OrderId,
    Guid CustomerId,
    Guid PaymentMethodId,
    decimal Amount,
    string CurrencyCode,
    DateTimeOffset ExpiresAtUtc) : IIntegrationCommand
{
    public const string EndpointName = "payment-authorize-order";
}
