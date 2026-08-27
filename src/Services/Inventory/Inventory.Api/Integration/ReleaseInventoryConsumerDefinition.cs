using MassTransit;
using Microservices.Contracts.Inventory.V1;

namespace Inventory.Api.Integration;

internal sealed class ReleaseInventoryConsumerDefinition : ConsumerDefinition<ReleaseInventoryConsumer>
{
    public ReleaseInventoryConsumerDefinition() => EndpointName = ReleaseInventory.EndpointName;
}
