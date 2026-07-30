using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerHttpResults
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

        var descriptor = CustomerErrorCatalog.GetRequired(error);
        foreach (var metadata in error.Metadata)
        {
            if (ReservedProblemProperties.Contains(metadata.Key))
            {
                throw new InvalidOperationException(
                    $"Error metadata key '{metadata.Key}' conflicts with the Customer Problem Details contract.");
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
}
