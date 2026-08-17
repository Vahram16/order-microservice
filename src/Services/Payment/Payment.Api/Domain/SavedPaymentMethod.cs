namespace Payment.Api.Domain;

public sealed class SavedPaymentMethod
{
    private SavedPaymentMethod()
    {
    }

    private SavedPaymentMethod(
        Guid id,
        Guid customerId,
        string providerPaymentMethodId,
        string type,
        string? brand,
        string? last4,
        int? expMonth,
        int? expYear,
        string? walletType,
        bool isDefault,
        DateTimeOffset now)
    {
        Id = id;
        CustomerId = customerId;
        ProviderPaymentMethodId = providerPaymentMethodId;
        Type = type;
        Brand = brand;
        Last4 = last4;
        ExpMonth = expMonth;
        ExpYear = expYear;
        WalletType = walletType;
        IsDefault = isDefault;
        Status = SavedPaymentMethodStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string ProviderPaymentMethodId { get; private set; } = string.Empty;
    public string Type { get; private set; } = string.Empty;
    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpMonth { get; private set; }
    public int? ExpYear { get; private set; }
    public string? WalletType { get; private set; }
    public bool IsDefault { get; private set; }
    public SavedPaymentMethodStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SavedPaymentMethod Create(
        Guid customerId,
        string providerPaymentMethodId,
        string type,
        string? brand,
        string? last4,
        int? expMonth,
        int? expYear,
        string? walletType,
        bool isDefault,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            customerId,
            NormalizeRequired(providerPaymentMethodId),
            NormalizeRequired(type),
            NormalizeOptional(brand),
            NormalizeOptional(last4),
            expMonth,
            expYear,
            NormalizeOptional(walletType),
            isDefault,
            now);

    public void Synchronize(
        string type,
        string? brand,
        string? last4,
        int? expMonth,
        int? expYear,
        string? walletType,
        DateTimeOffset now)
    {
        Type = NormalizeRequired(type);
        Brand = NormalizeOptional(brand);
        Last4 = NormalizeOptional(last4);
        ExpMonth = expMonth;
        ExpYear = expYear;
        WalletType = NormalizeOptional(walletType);
        Status = SavedPaymentMethodStatus.Active;
        UpdatedAt = now;
    }

    public void MakeDefault(DateTimeOffset now)
    {
        IsDefault = true;
        UpdatedAt = now;
    }

    public void ClearDefault(DateTimeOffset now)
    {
        IsDefault = false;
        UpdatedAt = now;
    }

    private static string NormalizeRequired(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum SavedPaymentMethodStatus
{
    Active = 1,
    Detached = 2
}
