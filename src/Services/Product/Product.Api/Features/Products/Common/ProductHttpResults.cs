using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Product.Api.Features.Products.Common;

internal static class ProductHttpResults
{
    private static readonly HashSet<string> ReservedProblemProperties = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "type", "title", "status", "detail", "instance", "code", "retryable", "traceId"
    };

    public static IResult Problem(OperationError error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var descriptor = ProductErrorCatalog.GetRequired(error);
        foreach (var metadata in error.Metadata)
        {
            if (ReservedProblemProperties.Contains(metadata.Key))
            {
                throw new InvalidOperationException(
                    $"Error metadata key '{metadata.Key}' conflicts with the Product Problem Details contract.");
            }
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
