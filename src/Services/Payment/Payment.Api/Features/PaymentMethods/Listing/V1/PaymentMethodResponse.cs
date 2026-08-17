namespace Payment.Api.Features.PaymentMethods.Listing.V1;

public sealed record PaymentMethodResponse(
    Guid Id,
    string Type,
    string? Brand,
    string? Last4,
    int? ExpMonth,
    int? ExpYear,
    string? WalletType,
    bool IsDefault);
