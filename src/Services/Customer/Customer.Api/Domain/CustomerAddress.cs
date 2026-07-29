namespace Customer.Api.Domain;

public sealed class CustomerAddress
{
    private CustomerAddress()
    {
    }

    private CustomerAddress(
        Guid id,
        Guid customerId,
        NormalizedAddressData data,
        DateTimeOffset now)
    {
        Id = id;
        CustomerId = customerId;
        Apply(data);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string? Label { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = null!;
    public string? Region { get; private set; }
    public string PostalCode { get; private set; } = null!;
    public CountryCode CountryCode { get; private set; }
    public string? PhoneNumber { get; private set; }
    public bool IsDefaultShipping { get; private set; }
    public bool IsDefaultBilling { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static CustomerAddress Create(
        Guid id,
        Guid customerId,
        AddressData data,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            throw new CustomerDomainException(
                "customer.invalid_address_id",
                "Address identifier cannot be empty.");
        }

        return new CustomerAddress(id, customerId, NormalizedAddressData.From(data), now);
    }

    internal void Update(AddressData data, DateTimeOffset now)
    {
        Apply(NormalizedAddressData.From(data));
        UpdatedAt = LaterOf(UpdatedAt, now);
    }

    internal bool Matches(AddressData data)
    {
        var candidate = NormalizedAddressData.From(data);
        return Label == candidate.Label &&
               RecipientName == candidate.RecipientName &&
               Line1 == candidate.Line1 &&
               Line2 == candidate.Line2 &&
               City == candidate.City &&
               Region == candidate.Region &&
               PostalCode == candidate.PostalCode &&
               CountryCode == candidate.CountryCode &&
               PhoneNumber == candidate.PhoneNumber &&
               IsDefaultShipping == candidate.IsDefaultShipping &&
               IsDefaultBilling == candidate.IsDefaultBilling;
    }

    internal void ClearDefaultShipping(DateTimeOffset now)
    {
        IsDefaultShipping = false;
        UpdatedAt = LaterOf(UpdatedAt, now);
    }

    internal void ClearDefaultBilling(DateTimeOffset now)
    {
        IsDefaultBilling = false;
        UpdatedAt = LaterOf(UpdatedAt, now);
    }

    private void Apply(NormalizedAddressData data)
    {
        Label = data.Label;
        RecipientName = data.RecipientName;
        Line1 = data.Line1;
        Line2 = data.Line2;
        City = data.City;
        Region = data.Region;
        PostalCode = data.PostalCode;
        CountryCode = data.CountryCode;
        PhoneNumber = data.PhoneNumber;
        IsDefaultShipping = data.IsDefaultShipping;
        IsDefaultBilling = data.IsDefaultBilling;
    }

    private static DateTimeOffset LaterOf(DateTimeOffset current, DateTimeOffset candidate) =>
        candidate > current ? candidate : current;
}

public sealed record AddressData(
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

internal sealed record NormalizedAddressData(
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    CountryCode CountryCode,
    string? PhoneNumber,
    bool IsDefaultShipping,
    bool IsDefaultBilling)
{
    public static NormalizedAddressData From(AddressData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new NormalizedAddressData(
            Optional(data.Label, nameof(data.Label), 50),
            Required(data.RecipientName, nameof(data.RecipientName), 200),
            Required(data.Line1, nameof(data.Line1), 200),
            Optional(data.Line2, nameof(data.Line2), 200),
            Required(data.City, nameof(data.City), 100),
            Optional(data.Region, nameof(data.Region), 100),
            Required(data.PostalCode, nameof(data.PostalCode), 32),
            CountryCode.Parse(data.CountryCode),
            Optional(data.PhoneNumber, nameof(data.PhoneNumber), 32),
            data.IsDefaultShipping,
            data.IsDefaultBilling);
    }

    private static string Required(string value, string field, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new CustomerDomainException(
                "customer.validation",
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(string? value, string field, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, field, maximumLength);
}
