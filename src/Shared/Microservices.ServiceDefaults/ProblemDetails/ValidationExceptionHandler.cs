using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Microservices.ServiceDefaults.ProblemDetails;

internal sealed class ValidationExceptionHandler : IExceptionHandler
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
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        var descriptor = PlatformProblemCatalog.ValidationFailed;
        var problem = new HttpValidationProblemDetails(errors)
        {
            Type = descriptor.Type,
            Title = descriptor.Title,
            Status = descriptor.Status,
            Detail = descriptor.Description,
            Instance = httpContext.Request.Path.Value
        };
        problem.Extensions["code"] = descriptor.Code;
        problem.Extensions["retryable"] = descriptor.Retryable;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        await PlatformProblemDetailsWriter.WriteAsync(
            httpContext,
            problem,
            exception,
            cancellationToken);
        return true;
    }
}
