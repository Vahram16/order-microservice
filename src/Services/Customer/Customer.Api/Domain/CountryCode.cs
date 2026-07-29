namespace Customer.Api.Domain;

public readonly record struct CountryCode
{
    private CountryCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CountryCode Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new CustomerDomainException(
                "customer.invalid_country_code",
                "CountryCode must be an ISO 3166-1 alpha-2 code.");
        }

        return new CountryCode(normalized);
    }

    public override string ToString() => Value;
}
