using Microservices.Application;
using Order.Api.Features.Orders.Common;

namespace Order.Api.Features.Orders.GettingById.V1;

internal sealed record GetOrderByIdQuery(Guid OrderId, string IdentityProvider, string IdentitySubject)
    : IQuery<Result<OrderResponse>>;
