namespace Microservices.Primitives;

public static class CurrencyAmount
{
    private static readonly HashSet<string> WholeUnitCurrencies = new(StringComparer.Ordinal)
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "MGA",
        "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF"
    };

    public static bool TryNormalizeCurrencyCode(string? value, out string currencyCode)
    {
        currencyCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is not (>= 'A' and <= 'Z')))
        {
            return false;
        }

        currencyCode = normalized;
        return true;
    }

    public static bool UsesWholeUnits(string currencyCode) =>
        TryNormalizeCurrencyCode(currencyCode, out var normalized) && WholeUnitCurrencies.Contains(normalized);

    public static bool HasValidScale(decimal amount, string currencyCode)
    {
        if (!TryNormalizeCurrencyCode(currencyCode, out var normalized))
        {
            return false;
        }

        var decimals = WholeUnitCurrencies.Contains(normalized) ? 0 : 2;
        return decimal.Round(amount, decimals) == amount;
    }
}
