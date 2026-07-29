using Microsoft.Extensions.Primitives;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerHttp
{
    private const string EtagPrefix = "\"customer-";
    private const string EtagSuffix = "\"";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static long RequireExpectedVersion(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values) || values.Count != 1)
        {
            throw new CustomerPreconditionRequiredException();
        }

        var value = values[0]?.Trim();
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("W/", StringComparison.OrdinalIgnoreCase) ||
            !value.StartsWith(EtagPrefix, StringComparison.Ordinal) ||
            !value.EndsWith(EtagSuffix, StringComparison.Ordinal))
        {
            throw new CustomerInvalidPreconditionException();
        }

        var versionText = value[EtagPrefix.Length..^EtagSuffix.Length];
        if (!long.TryParse(versionText, out var version) || version <= 0)
        {
            throw new CustomerInvalidPreconditionException();
        }

        return version;
    }

    public static Guid RequireIdempotencyKey(HttpRequest request)
    {
        var values = request.Headers[IdempotencyHeader];
        if (StringValues.IsNullOrEmpty(values) ||
            values.Count != 1 ||
            !Guid.TryParse(values[0], out var key) ||
            key == Guid.Empty)
        {
            throw new CustomerInvalidIdempotencyKeyException();
        }

        return key;
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

internal sealed class CustomerPreconditionRequiredException()
    : Exception("An If-Match header containing the current customer ETag is required.");

internal sealed class CustomerInvalidPreconditionException()
    : Exception("If-Match must contain exactly one strong customer ETag.");

internal sealed class CustomerInvalidIdempotencyKeyException()
    : Exception("Idempotency-Key must contain exactly one non-empty GUID.");
