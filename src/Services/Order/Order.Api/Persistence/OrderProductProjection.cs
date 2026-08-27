namespace Order.Api.Persistence;

internal sealed class OrderProductProjection
{
    private OrderProductProjection() { }

    private OrderProductProjection(
        Guid productId,
        string sku,
        string name,
        decimal price,
        string currencyCode,
        long sourceVersion,
        bool isAvailable,
        DateTimeOffset updatedAt)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        Price = price;
        CurrencyCode = currencyCode;
        SourceVersion = sourceVersion;
        IsAvailable = isAvailable;
        UpdatedAt = updatedAt;
    }

    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public long SourceVersion { get; private set; }
    public bool IsAvailable { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OrderProductProjection Create(
        Guid productId,
        string sku,
        string name,
        decimal price,
        string currencyCode,
        long sourceVersion,
        bool isAvailable,
        DateTimeOffset now) =>
        new(productId, sku, name, price, currencyCode, sourceVersion, isAvailable, now);

    public void Apply(string sku, string name, decimal price, string currencyCode, long sourceVersion, bool isAvailable, DateTimeOffset now)
    {
        if (sourceVersion <= SourceVersion)
        {
            return;
        }

        Sku = sku;
        Name = name;
        Price = price;
        CurrencyCode = currencyCode;
        SourceVersion = sourceVersion;
        IsAvailable = isAvailable;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
    }
}
