using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerHttp
{
    private const string EtagPrefix = "\"customer-";
    private const string EtagSuffix = "\"";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static Result<long> ReadExpectedVersion(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values))
        {
            return CustomerApplicationErrors.PreconditionRequired;
        }

        if (values.Count != 1)
        {
            return CustomerApplicationErrors.InvalidPrecondition;
        }

        var value = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            !value.StartsWith(EtagPrefix, StringComparison.Ordinal) ||
            !value.EndsWith(EtagSuffix, StringComparison.Ordinal))
        {
            return CustomerApplicationErrors.InvalidPrecondition;
        }

        var versionText = value[EtagPrefix.Length..^EtagSuffix.Length];
        return long.TryParse(
                   versionText,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out var version) &&
               version > 0
            ? Result.Success(version)
            : CustomerApplicationErrors.InvalidPrecondition;
    }

    public static Result<Guid> ReadIdempotencyKey(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers[IdempotencyHeader];
        return !StringValues.IsNullOrEmpty(values) &&
               values.Count == 1 &&
               Guid.TryParse(values[0], out var key) &&
               key != Guid.Empty
            ? Result.Success(key)
            : CustomerApplicationErrors.InvalidIdempotencyKey;
    }

    public static void WriteEtag(HttpResponse response, long version) =>
        response.Headers.ETag = FormatEtag(version);

    public static string FormatEtag(long version) => $"\"customer-{version}\"";

    public static async ValueTask<object?> AddSensitiveResponseHeadersAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return await next(context);
    }
}
