using Microservices.Primitives;

namespace Payment.Api.Domain;

public sealed class PaymentMethod
{
    private PaymentMethod() { }

    public Guid Id { get; private set; }
    public Guid PaymentCustomerId { get; private set; }
    public string ProviderPaymentMethodId { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Last4 { get; private set; } = string.Empty;
    public int ExpMonth { get; private set; }
    public int ExpYear { get; private set; }
    public string? WalletType { get; private set; }
    public PaymentMethodStatus Status { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<PaymentMethod> Create(
        Guid id,
        Guid paymentCustomerId,
        string providerPaymentMethodId,
        CardPaymentMethodDetails card,
        bool isDefault,
        DateTimeOffset now)
    {
        var validation = Validate(id, paymentCustomerId, providerPaymentMethodId, card);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return Result.Success(new PaymentMethod
        {
            Id = id,
            PaymentCustomerId = paymentCustomerId,
            ProviderPaymentMethodId = providerPaymentMethodId,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpMonth = card.ExpMonth,
            ExpYear = card.ExpYear,
            WalletType = card.WalletType,
            Status = PaymentMethodStatus.Active,
            IsDefault = isDefault,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    public Result Synchronize(CardPaymentMethodDetails card, DateTimeOffset now)
    {
        var validation = ValidateCard(card);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        Brand = card.Brand;
        Last4 = card.Last4;
        ExpMonth = card.ExpMonth;
        ExpYear = card.ExpYear;
        WalletType = card.WalletType;
        Status = PaymentMethodStatus.Active;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MakeDefault(DateTimeOffset now)
    {
        if (Status != PaymentMethodStatus.Active)
        {
            return PaymentErrors.PaymentMethodInactive;
        }

        if (!IsDefault)
        {
            IsDefault = true;
            UpdatedAt = now;
        }

        return Result.Success();
    }

    public void ClearDefault(DateTimeOffset now)
    {
        if (!IsDefault)
        {
            return;
        }

        IsDefault = false;
        UpdatedAt = now;
    }

    public void MarkDetached(DateTimeOffset now)
    {
        Status = PaymentMethodStatus.Detached;
        IsDefault = false;
        UpdatedAt = now;
    }

    private static Result Validate(
        Guid id,
        Guid paymentCustomerId,
        string providerPaymentMethodId,
        CardPaymentMethodDetails card)
    {
        if (id == Guid.Empty)
        {
            return PaymentErrors.Validation(nameof(id), "Payment method identifier cannot be empty.");
        }

        if (paymentCustomerId == Guid.Empty)
        {
            return PaymentErrors.Validation(
                nameof(paymentCustomerId),
                "Payment customer identifier cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(providerPaymentMethodId) ||
            !string.Equals(providerPaymentMethodId, providerPaymentMethodId.Trim(), StringComparison.Ordinal) ||
            providerPaymentMethodId.Length > 255)
        {
            return PaymentErrors.Validation(
                nameof(providerPaymentMethodId),
                "Provider payment method identifier is invalid.");
        }

        return ValidateCard(card);
    }

    private static Result ValidateCard(CardPaymentMethodDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Brand) || card.Brand.Length > 32)
        {
            return PaymentErrors.Validation(nameof(card.Brand), "Card brand is invalid.");
        }

        if (card.Last4.Length != 4 || card.Last4.Any(character => !char.IsAsciiDigit(character)))
        {
            return PaymentErrors.Validation(nameof(card.Last4), "Card last4 must contain four digits.");
        }

        if (card.ExpMonth is < 1 or > 12)
        {
            return PaymentErrors.Validation(nameof(card.ExpMonth), "Card expiry month is invalid.");
        }

        if (card.ExpYear is < 2000 or > 9999)
        {
            return PaymentErrors.Validation(nameof(card.ExpYear), "Card expiry year is invalid.");
        }

        if (card.WalletType is { Length: > 32 })
        {
            return PaymentErrors.Validation(nameof(card.WalletType), "Wallet type is invalid.");
        }

        return Result.Success();
    }
}
