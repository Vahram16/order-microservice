using Microservices.Primitives;

namespace Payment.Api.Infrastructure.Stripe;

internal static class StripeMoneyConverter
{
    public static bool TryToProviderUnits(decimal amount, string currencyCode, out long providerUnits)
    {
        providerUnits = 0;
        if (!CurrencyAmount.TryNormalizeCurrencyCode(currencyCode, out var currency) || amount <= 0m || !CurrencyAmount.HasValidScale(amount, currency))
            return false;

        var multiplier = currency switch
        {
            "ISK" or "UGX" => 100m,
            _ when CurrencyAmount.UsesWholeUnits(currency) => 1m,
            _ => 100m
        };
        var scaled = amount * multiplier;
        if (scaled > long.MaxValue || scaled < long.MinValue || decimal.Truncate(scaled) != scaled) return false;
        providerUnits = decimal.ToInt64(scaled);
        return true;
    }

    public static bool TryFromProviderUnits(long providerUnits, string currencyCode, out decimal amount)
    {
        amount = 0m;
        if (!CurrencyAmount.TryNormalizeCurrencyCode(currencyCode, out var currency)) return false;
        var divisor = currency switch
        {
            "ISK" or "UGX" => 100m,
            _ when CurrencyAmount.UsesWholeUnits(currency) => 1m,
            _ => 100m
        };
        if ((currency is "ISK" or "UGX") && providerUnits % 100 != 0) return false;
        amount = providerUnits / divisor;
        return CurrencyAmount.HasValidScale(amount, currency);
    }
}
