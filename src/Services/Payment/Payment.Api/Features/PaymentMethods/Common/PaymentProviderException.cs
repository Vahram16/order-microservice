namespace Payment.Api.Features.PaymentMethods.Common;

internal sealed class PaymentProviderException : Exception
{
    private PaymentProviderException(
        string code,
        PaymentProviderFailureKind failureKind,
        Exception innerException)
        : base("The payment provider request failed.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        FailureKind = failureKind;
    }

    public string Code { get; }
    public PaymentProviderFailureKind FailureKind { get; }

    public static PaymentProviderException Permanent(string code, Exception innerException) =>
        new(code, PaymentProviderFailureKind.Permanent, innerException);

    public static PaymentProviderException Transient(string code, Exception innerException) =>
        new(code, PaymentProviderFailureKind.Transient, innerException);
}

internal enum PaymentProviderFailureKind
{
    Permanent,
    Transient
}
