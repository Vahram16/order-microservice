using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Order.Api.Features.Orders.Common;

internal static class OrderHttpResults
{
    public static IResult Problem(OperationError error, HttpContext context)
    {
        var descriptor = OrderErrorCatalog.GetRequired(error);
        var problem = new ProblemDetails
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.Status,
            Detail = error.PublicDescription,
            Instance = context.Request.Path.Value
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["retryable"] = descriptor.Retryable;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return Results.Problem(problem);
    }
}
