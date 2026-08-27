using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Api.Integration;

internal sealed class InventoryPersistenceExceptionRule : IConsumerExceptionRule
{
    public ConsumerExceptionDisposition Classify(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => ConsumerExceptionDisposition.Transient,
        PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } =>
            ConsumerExceptionDisposition.Transient,
        _ => ConsumerExceptionDisposition.Unclassified
    };
}
