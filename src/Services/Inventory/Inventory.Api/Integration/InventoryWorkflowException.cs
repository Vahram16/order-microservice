using Microservices.Messaging;

namespace Inventory.Api.Integration;

internal sealed class InventoryWorkflowException(string code)
    : Exception(code), IPermanentConsumerFailure;
