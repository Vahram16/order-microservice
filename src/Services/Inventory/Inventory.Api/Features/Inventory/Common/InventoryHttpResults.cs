using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Features.Inventory.Common;

internal static class InventoryHttpResults
{
    public static IResult Problem(OperationError error, HttpContext context)
    {
        var status = error.Category switch
        {
            ErrorCategory.InvalidInput => StatusCodes.Status400BadRequest,
            ErrorCategory.MissingResource => StatusCodes.Status404NotFound,
            ErrorCategory.StateConflict => StatusCodes.Status409Conflict,
            ErrorCategory.ConcurrencyConflict => StatusCodes.Status412PreconditionFailed,
            ErrorCategory.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Type = $"/errors/v1/inventory/{error.Code}",
            Title = "Inventory request failed",
            Status = status,
            Detail = error.PublicDescription,
            Instance = context.Request.Path.Value
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        return Results.Problem(problem);
    }
}
