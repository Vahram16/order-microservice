namespace Payment.Api.Features.PaymentMethods.Common;

public sealed record PaymentMethodResponse(Guid Id, string Brand, string Last4, int ExpMonth, int ExpYear, string? WalletType, bool IsDefault, string Status);
