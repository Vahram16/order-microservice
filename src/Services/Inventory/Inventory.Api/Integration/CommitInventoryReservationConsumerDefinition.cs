using MassTransit;
using Microservices.Contracts.Inventory.V1;

namespace Inventory.Api.Integration;

internal sealed class CommitInventoryReservationConsumerDefinition : ConsumerDefinition<CommitInventoryReservationConsumer>
{
    public CommitInventoryReservationConsumerDefinition() => EndpointName = CommitInventoryReservation.EndpointName;
}
