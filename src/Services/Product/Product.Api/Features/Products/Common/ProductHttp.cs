using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Product.Api.Features.Products.Common;

internal static class ProductHttp
{
    private const string EtagSuffix = "\"";

    public static Result<long> ReadExpectedVersion(HttpRequest request, Guid productId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values))
        {
            return ProductApplicationErrors.PreconditionRequired;
        }

        if (values.Count != 1)
        {
            return ProductApplicationErrors.InvalidPrecondition;
        }

        var value = values[0]?.Trim();
        var etagPrefix = $"\"product-{productId:N}-";
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            !value.StartsWith(etagPrefix, StringComparison.Ordinal) ||
            !value.EndsWith(EtagSuffix, StringComparison.Ordinal))
        {
            return ProductApplicationErrors.InvalidPrecondition;
        }

        var versionText = value[etagPrefix.Length..^EtagSuffix.Length];
        return long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) &&
               version > 0
            ? Result.Success(version)
            : ProductApplicationErrors.InvalidPrecondition;
    }

    public static void WriteEtag(HttpResponse response, Guid productId, long version) =>
        response.Headers.ETag = FormatEtag(productId, version);

    public static string FormatEtag(Guid productId, long version) =>
        $"\"product-{productId:N}-{version}\"";
}
