using Microservices.Primitives;

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

    internal static Result<CustomerAddress> Create(
        Guid id,
        Guid customerId,
        AddressData data,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (id == Guid.Empty)
        {
            return CustomerErrors.InvalidAddressId;
        }

        var normalized = NormalizedAddressData.Create(data);
        return normalized.IsSuccess
            ? Result.Success(new CustomerAddress(id, customerId, normalized.Value, now))
            : normalized.Error;
    }

    internal Result Update(AddressData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var normalized = NormalizedAddressData.Create(data);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        Apply(normalized.Value);
        UpdatedAt = LaterOf(UpdatedAt, now);
        return Result.Success();
    }

    internal Result<bool> Matches(AddressData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var normalized = NormalizedAddressData.Create(data);
        if (normalized.IsFailure)
        {
            return normalized.Error;
        }

        var candidate = normalized.Value;
        var matches = Label == candidate.Label &&
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

        return Result.Success(matches);
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
    public static Result<NormalizedAddressData> Create(AddressData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var label = Optional(data.Label, nameof(data.Label), 50);
        if (label.IsFailure)
        {
            return label.Error;
        }

        var recipientName = Required(data.RecipientName, nameof(data.RecipientName), 200);
        if (recipientName.IsFailure)
        {
            return recipientName.Error;
        }

        var line1 = Required(data.Line1, nameof(data.Line1), 200);
        if (line1.IsFailure)
        {
            return line1.Error;
        }

        var line2 = Optional(data.Line2, nameof(data.Line2), 200);
        if (line2.IsFailure)
        {
            return line2.Error;
        }

        var city = Required(data.City, nameof(data.City), 100);
        if (city.IsFailure)
        {
            return city.Error;
        }

        var region = Optional(data.Region, nameof(data.Region), 100);
        if (region.IsFailure)
        {
            return region.Error;
        }

        var postalCode = Required(data.PostalCode, nameof(data.PostalCode), 32);
        if (postalCode.IsFailure)
        {
            return postalCode.Error;
        }

        var countryCode = CountryCode.Create(data.CountryCode);
        if (countryCode.IsFailure)
        {
            return countryCode.Error;
        }

        var phoneNumber = Optional(data.PhoneNumber, nameof(data.PhoneNumber), 32);
        if (phoneNumber.IsFailure)
        {
            return phoneNumber.Error;
        }

        return Result.Success(new NormalizedAddressData(
            label.Value.Value,
            recipientName.Value,
            line1.Value,
            line2.Value.Value,
            city.Value,
            region.Value.Value,
            postalCode.Value,
            countryCode.Value,
            phoneNumber.Value.Value,
            data.IsDefaultShipping,
            data.IsDefaultBilling));
    }

    private static Result<string> Required(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CustomerErrors.Validation(field, "A value is required.");
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? Result.Success(normalized)
            : CustomerErrors.Validation(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private static Result<OptionalText> Optional(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(new OptionalText(null));
        }

        var required = Required(value, field, maximumLength);
        return required.IsSuccess
            ? Result.Success(new OptionalText(required.Value))
            : required.Error;
    }

    private readonly record struct OptionalText(string? Value);
}
