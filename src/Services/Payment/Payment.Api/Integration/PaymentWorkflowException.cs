using Microservices.Messaging;

namespace Payment.Api.Integration;

internal abstract class PaymentWorkflowException(string code, Exception? innerException = null)
    : Exception(code, innerException)
{
    public static Exception Transient(string code, Exception? innerException = null) => new TransientFailure(code, innerException);
    public static Exception Permanent(string code, Exception? innerException = null) => new PermanentFailure(code, innerException);

    private sealed class TransientFailure(string code, Exception? innerException)
        : PaymentWorkflowException(code, innerException), ITransientConsumerFailure;

    private sealed class PermanentFailure(string code, Exception? innerException)
        : PaymentWorkflowException(code, innerException), IPermanentConsumerFailure;
}
