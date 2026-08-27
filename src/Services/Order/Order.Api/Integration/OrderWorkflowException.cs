using Microservices.Messaging;

namespace Order.Api.Integration;

internal sealed class OrderWorkflowException(string code)
    : Exception(code), IPermanentConsumerFailure;
