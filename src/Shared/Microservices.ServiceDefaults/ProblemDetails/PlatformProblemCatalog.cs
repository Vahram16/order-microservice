using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Microservices.ServiceDefaults.ProblemDetails;

internal static class PlatformProblemCatalog
{
    internal static PlatformProblemDescriptor ValidationFailed { get; } = new(
        "request.validation_failed",
        "Validation failed",
        StatusCodes.Status400BadRequest,
        "One or more request values are invalid.",
        false);

    internal static PlatformProblemDescriptor Unexpected { get; } = new(
        "server.unexpected",
        "Internal Server Error",
        StatusCodes.Status500InternalServerError,
        "An unexpected error occurred.",
        true);

    internal static PlatformProblemDescriptor ForStatusCode(int statusCode)
    {
        if (statusCode is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(statusCode),
                statusCode,
                "Problem status codes must be between 400 and 599.");
        }

        var detail = statusCode switch
        {
            StatusCodes.Status401Unauthorized => "Authentication is required to access this resource.",
            StatusCodes.Status403Forbidden => "The authenticated caller is not allowed to access this resource.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status405MethodNotAllowed => "The HTTP method is not allowed for this resource.",
            StatusCodes.Status500InternalServerError => Unexpected.Description,
            StatusCodes.Status502BadGateway => "An upstream service returned an invalid response.",
            StatusCodes.Status503ServiceUnavailable => "The service is temporarily unavailable.",
            StatusCodes.Status504GatewayTimeout => "An upstream service did not respond in time.",
            _ when statusCode >= StatusCodes.Status500InternalServerError =>
                "The server could not complete the request.",
            _ => "The request could not be completed."
        };

        var title = ReasonPhrases.GetReasonPhrase(statusCode);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"HTTP {statusCode}";
        }

        return new PlatformProblemDescriptor(
            $"http.status.{statusCode}",
            title,
            statusCode,
            detail,
            statusCode is StatusCodes.Status408RequestTimeout or
                StatusCodes.Status429TooManyRequests or
                StatusCodes.Status500InternalServerError or
                StatusCodes.Status502BadGateway or
                StatusCodes.Status503ServiceUnavailable or
                StatusCodes.Status504GatewayTimeout);
    }

    internal static bool TryResolve(string code, out PlatformProblemDescriptor descriptor)
    {
        if (string.Equals(code, ValidationFailed.Code, StringComparison.Ordinal))
        {
            descriptor = ValidationFailed;
            return true;
        }

        if (string.Equals(code, Unexpected.Code, StringComparison.Ordinal))
        {
            descriptor = Unexpected;
            return true;
        }

        const string statusPrefix = "http.status.";
        if (code.StartsWith(statusPrefix, StringComparison.Ordinal) &&
            int.TryParse(
                code[statusPrefix.Length..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var statusCode) &&
            statusCode is >= 400 and <= 599 &&
            string.Equals(
                code,
                statusPrefix + statusCode.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            descriptor = ForStatusCode(statusCode);
            return true;
        }

        descriptor = null!;
        return false;
    }
}
