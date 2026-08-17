namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed record CreatePaymentMethodSetupResult(
    Guid RequestId,
    string SetupIntentId,
    string ClientSecret);
