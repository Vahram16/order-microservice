namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed record CreatePaymentMethodSetupResult(
    string SetupId,
    string ClientSecret,
    string Status);
