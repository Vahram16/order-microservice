using Microservices.Application;
using Order.Api.Features.Orders.Common;

namespace Order.Api.Features.Orders.Creating.V1;

internal sealed record CreateOrderCommand(
    Guid IdempotencyKey,
    string IdentityProvider,
    string IdentitySubject,
    IReadOnlyList<CreateOrderItemRequest> Items,
    Guid PaymentMethodId,
    CreateOrderShippingAddressRequest ShippingAddress)
    : ICommand<Result<OrderResponse>>;
