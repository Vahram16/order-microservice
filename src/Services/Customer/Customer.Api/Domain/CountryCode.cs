using Microservices.Primitives;

namespace Customer.Api.Domain;

public readonly record struct CountryCode
{
    private CountryCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static Result<CountryCode> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CustomerErrors.Validation(nameof(CountryCode), "A value is required.");
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => !char.IsAsciiLetter(character)))
        {
            return CustomerErrors.InvalidCountryCode;
        }

        return Result.Success(new CountryCode(normalized));
    }

    internal static CountryCode FromPersistence(string value)
    {
        var result = Create(value);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"Persisted CountryCode '{value}' violates the domain invariant.");
    }

    public override string ToString() => Value;
}
