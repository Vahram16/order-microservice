using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace Notifications.Api.Infrastructure;

internal sealed class NotificationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
        {
            return false;
        }

        var errors = validationException.Errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Notification request validation failed")
            .ExecuteAsync(httpContext);
        return true;
    }
}
