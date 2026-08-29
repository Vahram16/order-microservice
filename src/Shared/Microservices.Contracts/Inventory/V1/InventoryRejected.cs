using Microservices.Contracts;

namespace Microservices.Contracts.Inventory.V1;

public sealed record InventoryRejected(
    Guid OrderId,
    string ReasonCode,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent;
