namespace Payment.Api.Features.PaymentMethods.Common;

internal sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string code, Exception innerException) : base("The payment provider request failed.", innerException) { ArgumentException.ThrowIfNullOrWhiteSpace(code); Code = code; }
    public string Code { get; }
}
