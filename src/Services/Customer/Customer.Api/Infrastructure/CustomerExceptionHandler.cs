using Customer.Api.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Infrastructure;

internal sealed class CustomerExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<CustomerExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => CreateValidationProblem(validation),
            CustomerNotFoundException => CreateProblem(
                StatusCodes.Status404NotFound,
                "Customer not found"),
            CustomerAddressNotFoundException => CreateProblem(
                StatusCodes.Status404NotFound,
                "Customer address not found"),
            DbUpdateConcurrencyException => CreateProblem(
                StatusCodes.Status409Conflict,
                "The customer was modified by another request. Reload and retry."),
            CustomerDomainException domain => CreateProblem(
                StatusCodes.Status400BadRequest,
                domain.Message),
            UnauthorizedAccessException => CreateProblem(
                StatusCodes.Status401Unauthorized,
                "A valid user access token is required."),
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Customer API request failed.");
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails CreateProblem(int status, string detail) => new()
    {
        Status = status,
        Title = ReasonPhrases.GetReasonPhrase(status),
        Detail = detail
    };

    private static HttpValidationProblemDetails CreateValidationProblem(
        ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        return new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed"
        };
    }
}
