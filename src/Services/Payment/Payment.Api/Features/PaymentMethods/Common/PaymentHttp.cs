using Microsoft.Extensions.Primitives;

namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentHttp
{
    public static Result<Guid> ReadIdempotencyKey(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Idempotency-Key", out StringValues values) ||
            values.Count != 1 ||
            !Guid.TryParse(values[0], out var key) ||
            key == Guid.Empty)
        {
            return PaymentApplicationErrors.InvalidIdempotencyKey;
        }

        return Result.Success(key);
    }

    public static IResult Problem(OperationError error, HttpContext httpContext)
    {
        var status = error.Category switch
        {
            ErrorCategory.InvalidInput => StatusCodes.Status400BadRequest,
            ErrorCategory.AuthenticationRequired => StatusCodes.Status401Unauthorized,
            ErrorCategory.AuthorizationDenied => StatusCodes.Status403Forbidden,
            ErrorCategory.MissingResource => StatusCodes.Status404NotFound,
            ErrorCategory.StateConflict => StatusCodes.Status409Conflict,
            ErrorCategory.ConcurrencyConflict => StatusCodes.Status409Conflict,
            ErrorCategory.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status == StatusCodes.Status401Unauthorized)
        {
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        }

        return Results.Problem(
            statusCode: status,
            title: error.Code,
            detail: error.PublicDescription,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = error.Code,
                ["traceId"] = httpContext.TraceIdentifier
            });
    }
}
