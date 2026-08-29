namespace Order.Api.Domain;

public sealed class ShippingAddress
{
    private ShippingAddress() { }

    private ShippingAddress(
        string recipientName,
        string line1,
        string? line2,
        string city,
        string? region,
        string postalCode,
        string countryCode,
        string? phoneNumber)
    {
        RecipientName = recipientName;
        Line1 = line1;
        Line2 = line2;
        City = city;
        Region = region;
        PostalCode = postalCode;
        CountryCode = countryCode;
        PhoneNumber = phoneNumber;
    }

    public string RecipientName { get; private set; } = null!;
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = null!;
    public string? Region { get; private set; }
    public string PostalCode { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }

    public static Result<ShippingAddress> Create(ShippingAddressData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var recipient = Required(data.RecipientName, 200);
        var line1 = Required(data.Line1, 200);
        var city = Required(data.City, 100);
        var postalCode = Required(data.PostalCode, 32);
        var line2 = Optional(data.Line2, 200);
        var region = Optional(data.Region, 100);
        var phone = Optional(data.PhoneNumber, 32);
        var country = data.CountryCode?.Trim().ToUpperInvariant();

        if (recipient is null || line1 is null || city is null || postalCode is null ||
            line2.IsFailure || region.IsFailure || phone.IsFailure ||
            country is null || country.Length != 2 ||
            country.Any(character => character is not (>= 'A' and <= 'Z')))
        {
            return OrderErrors.InvalidShippingAddress;
        }

        return Result.Success(new ShippingAddress(
            recipient,
            line1,
            line2.Value,
            city,
            region.Value,
            postalCode,
            country,
            phone.Value));
    }

    private static string? Required(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : null;
    }

    private static OptionalText Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new OptionalText(null, false);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? new OptionalText(normalized, false)
            : new OptionalText(null, true);
    }

    private readonly record struct OptionalText(string? Value, bool IsFailure);
}
