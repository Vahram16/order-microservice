using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentHttpResults
{
    private static readonly HashSet<string> ReservedProblemProperties = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "type",
        "title",
        "status",
        "detail",
        "instance",
        "code",
        "retryable",
        "traceId"
    };

    public static IResult Problem(OperationError error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var descriptor = PaymentErrorCatalog.GetRequired(error);
        foreach (var metadata in error.Metadata)
        {
            if (ReservedProblemProperties.Contains(metadata.Key))
            {
                throw new InvalidOperationException(
                    $"Error metadata key '{metadata.Key}' conflicts with the Payment Problem Details contract.");
            }
        }

        if (descriptor.Status == StatusCodes.Status401Unauthorized)
        {
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        }

        var problem = new ProblemDetails
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.Status,
            Detail = error.PublicDescription,
            Instance = httpContext.Request.Path.Value
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["retryable"] = descriptor.Retryable;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        foreach (var metadata in error.Metadata)
        {
            problem.Extensions.Add(metadata.Key, metadata.Value);
        }

        return Results.Problem(problem);
    }

    public static Result<Guid> ReadIdempotencyKey(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 ||
            !Guid.TryParseExact(values[0], "D", out var value) ||
            value == Guid.Empty)
        {
            return PaymentApplicationErrors.InvalidIdempotencyKey;
        }

        return Result.Success(value);
    }

    public static async ValueTask<object?> AddSensitiveResponseHeadersAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        return await next(context);
    }
}
