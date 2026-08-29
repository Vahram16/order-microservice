using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Inventory.Api.Features.Inventory.Common;

internal static class InventoryHttp
{
    public static Result<long?> ReadOptionalExpectedVersion(HttpRequest request, Guid productId)
    {
        var values = request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values))
        {
            return Result.Success<long?>(null);
        }

        if (values.Count != 1)
        {
            return InventoryApplicationErrors.InvalidPrecondition;
        }

        var value = values[0]?.Trim();
        var prefix = $"\"inventory-{productId:N}-";
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            !value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith('"'))
        {
            return InventoryApplicationErrors.InvalidPrecondition;
        }

        var versionText = value[prefix.Length..^1];
        return long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0
            ? Result.Success<long?>(version)
            : InventoryApplicationErrors.InvalidPrecondition;
    }

    public static void WriteEtag(HttpResponse response, Guid productId, long version) =>
        response.Headers.ETag = $"\"inventory-{productId:N}-{version}\"";
}
