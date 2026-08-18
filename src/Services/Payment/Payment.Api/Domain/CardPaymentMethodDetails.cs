namespace Payment.Api.Domain;

public sealed record CardPaymentMethodDetails(
    string Brand,
    string Last4,
    int ExpMonth,
    int ExpYear,
    string? WalletType);
