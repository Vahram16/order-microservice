using MassTransit;
using Microservices.Contracts.Inventory.V1;

namespace Inventory.Api.Integration;

internal sealed class ReserveInventoryConsumerDefinition : ConsumerDefinition<ReserveInventoryConsumer>
{
    public ReserveInventoryConsumerDefinition() => EndpointName = ReserveInventory.EndpointName;
}
