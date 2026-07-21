using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Infrastructure;

internal sealed partial class IdentityExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<IdentityExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => FromValidation(validation),
            IdentityOperationException identity => FromIdentity(identity),
            InvalidAccountTokenException invalidToken => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid account token",
                Detail = invalidToken.Message
            },
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        var statusCode = problem.Status ??
            throw new InvalidOperationException("A handled problem must have a status code.");
        if (logger.IsEnabled(LogLevel.Information))
        {
            var exceptionType = exception.GetType().Name;
            LogIdentityRequestRejected(
                logger,
                statusCode,
                exceptionType);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Identity request rejected with status {StatusCode} and exception {ExceptionType}")]
    private static partial void LogIdentityRequestRejected(
        ILogger logger,
        int statusCode,
        string exceptionType);

    private static HttpValidationProblemDetails FromValidation(
        ValidationException exception) =>
        new(exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Request validation failed"
        };

    private static HttpValidationProblemDetails FromIdentity(
        IdentityOperationException exception) =>
        new(exception.Errors
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).Distinct().ToArray(),
                StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Identity operation failed"
        };
}
