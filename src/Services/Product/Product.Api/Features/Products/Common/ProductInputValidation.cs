namespace Product.Api.Features.Products.Common;

internal static class ProductInputValidation
{
    internal static bool IsTrimmedLengthAtMost(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;

    internal static bool IsOptionalTrimmedLengthAtMost(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length <= maximumLength;

    internal static bool IsCurrencyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        return normalized.Length == 3 && normalized.All(character =>
            character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
    }
}
