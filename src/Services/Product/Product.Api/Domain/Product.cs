using Microservices.Primitives;

namespace Product.Api.Domain;

public sealed class Product
{
    public const int MaximumSkuLength = 64;
    public const int MaximumNameLength = 200;
    public const int MaximumDescriptionLength = 2000;
    public const decimal MaximumPrice = 9999999999999999.99m;

    private Product()
    {
    }

    private Product(
        Guid id,
        string sku,
        string name,
        string? description,
        decimal price,
        string currencyCode,
        DateTimeOffset now)
    {
        Id = id;
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        CurrencyCode = currencyCode;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Result<Product> Create(
        string sku,
        string name,
        string? description,
        decimal price,
        string currencyCode,
        DateTimeOffset now)
    {
        var values = Normalize(sku, name, description, price, currencyCode);
        if (values.IsFailure)
        {
            return values.Error;
        }

        return Result.Success(new Product(
            Guid.NewGuid(),
            values.Value.Sku,
            values.Value.Name,
            values.Value.Description,
            values.Value.Price,
            values.Value.CurrencyCode,
            now));
    }

    public Result EnsureExpectedVersion(long expectedVersion) =>
        expectedVersion > 0 && Version == expectedVersion
            ? Result.Success()
            : ProductErrors.VersionMismatch;

    public Result Update(
        string sku,
        string name,
        string? description,
        decimal price,
        string currencyCode,
        DateTimeOffset now)
    {
        var values = Normalize(sku, name, description, price, currencyCode);
        if (values.IsFailure)
        {
            return values.Error;
        }

        Sku = values.Value.Sku;
        Name = values.Value.Name;
        Description = values.Value.Description;
        Price = values.Value.Price;
        CurrencyCode = values.Value.CurrencyCode;
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
        return Result.Success();
    }

    private static Result<NormalizedProduct> Normalize(
        string? sku,
        string? name,
        string? description,
        decimal price,
        string? currencyCode)
    {
        var normalizedSku = Required(sku, "sku", MaximumSkuLength);
        if (normalizedSku.IsFailure)
        {
            return normalizedSku.Error;
        }

        var normalizedName = Required(name, "name", MaximumNameLength);
        if (normalizedName.IsFailure)
        {
            return normalizedName.Error;
        }

        var normalizedDescription = Optional(description, "description", MaximumDescriptionLength);
        if (normalizedDescription.IsFailure)
        {
            return normalizedDescription.Error;
        }

        if (price < 0m || price > MaximumPrice || decimal.Round(price, 2) != price)
        {
            return ProductErrors.InvalidPrice;
        }

        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return ProductErrors.InvalidCurrencyCode;
        }

        var normalizedCurrency = currencyCode.Trim();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(character =>
                character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')))
        {
            return ProductErrors.InvalidCurrencyCode;
        }

        return Result.Success(new NormalizedProduct(
            normalizedSku.Value.ToUpperInvariant(),
            normalizedName.Value,
            normalizedDescription.Value.Value,
            price,
            normalizedCurrency.ToUpperInvariant()));
    }

    private static Result<string> Required(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ProductErrors.Validation(field, "A value is required.");
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? Result.Success(normalized)
            : ProductErrors.Validation(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private static Result<OptionalText> Optional(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(new OptionalText(null));
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? Result.Success(new OptionalText(normalized))
            : ProductErrors.Validation(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private sealed record OptionalText(string? Value);

    private sealed record NormalizedProduct(
        string Sku,
        string Name,
        string? Description,
        decimal Price,
        string CurrencyCode);
}
