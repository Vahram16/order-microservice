using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Infrastructure;

internal sealed class CustomerExceptionHandler(
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => CreateValidationProblem(validation),
            CustomerNotFoundException notFound => CreateProblem(
                StatusCodes.Status404NotFound,
                notFound.Code,
                "Customer not found"),
            CustomerAddressNotFoundException notFound => CreateProblem(
                StatusCodes.Status404NotFound,
                notFound.Code,
                "Customer address not found"),
            CustomerPreconditionRequiredException required => CreateProblem(
                StatusCodes.Status428PreconditionRequired,
                "customer.precondition_required",
                required.Message),
            CustomerInvalidPreconditionException invalid => CreateProblem(
                StatusCodes.Status400BadRequest,
                "customer.invalid_precondition",
                invalid.Message),
            CustomerInvalidIdempotencyKeyException invalid => CreateProblem(
                StatusCodes.Status400BadRequest,
                "customer.invalid_idempotency_key",
                invalid.Message),
            CustomerVersionMismatchException mismatch => CreateProblem(
                StatusCodes.Status412PreconditionFailed,
                mismatch.Code,
                "The customer changed after it was read. Reload and retry."),
            DbUpdateConcurrencyException => CreateProblem(
                StatusCodes.Status412PreconditionFailed,
                "customer.version_mismatch",
                "The customer changed after it was read. Reload and retry."),
            CustomerIdempotencyConflictException conflict => CreateProblem(
                StatusCodes.Status409Conflict,
                conflict.Code,
                conflict.Message),
            CustomerInactiveException inactive => CreateProblem(
                StatusCodes.Status409Conflict,
                inactive.Code,
                inactive.Message),
            DbUpdateException update when update.IsUniqueConstraintViolation(
                CustomerDatabaseConstraints.Identity) => CreateProblem(
                    StatusCodes.Status409Conflict,
                    "customer.identity_conflict",
                    "A customer already exists for this identity."),
            DbUpdateException update when update.IsUniqueConstraintViolation(
                CustomerDatabaseConstraints.DefaultShipping) => CreateProblem(
                    StatusCodes.Status409Conflict,
                    "customer.default_shipping_conflict",
                    "Another request changed the default shipping address. Reload and retry."),
            DbUpdateException update when update.IsUniqueConstraintViolation(
                CustomerDatabaseConstraints.DefaultBilling) => CreateProblem(
                    StatusCodes.Status409Conflict,
                    "customer.default_billing_conflict",
                    "Another request changed the default billing address. Reload and retry."),
            DbUpdateException update when update.IsUniqueConstraintViolation(
                CustomerDatabaseConstraints.AddressPrimaryKey) => CreateProblem(
                    StatusCodes.Status409Conflict,
                    "customer.idempotency_conflict",
                    "The address idempotency key has already been used."),
            CustomerDomainException domain => CreateProblem(
                StatusCodes.Status400BadRequest,
                domain.Code,
                domain.Message),
            UnauthorizedAccessException => CreateProblem(
                StatusCodes.Status401Unauthorized,
                "customer.authentication_required",
                "A valid user access token is required."),
            _ => null
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails CreateProblem(int status, string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return problem;
    }

    private static HttpValidationProblemDetails CreateValidationProblem(
        ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed"
        };
        problem.Extensions["code"] = "customer.validation";
        return problem;
    }
}
