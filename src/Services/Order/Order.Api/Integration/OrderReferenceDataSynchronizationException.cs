using Microservices.Messaging;

namespace Order.Api.Integration;

internal sealed class OrderReferenceDataSynchronizationException(
    string code,
    Exception? innerException = null)
    : Exception(code, innerException), ITransientConsumerFailure;
