using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MvcProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace Microservices.ServiceDefaults.ProblemDetails;

internal static class PlatformProblemDetailsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static MvcProblemDetails Create(
        HttpContext httpContext,
        PlatformProblemDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(descriptor);

        var problem = new MvcProblemDetails
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
        return problem;
    }

    internal static async ValueTask WriteAsync(
        HttpContext httpContext,
        MvcProblemDetails problem,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });

        if (!written && !httpContext.Response.HasStarted)
        {
            httpContext.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                problem,
                problem.GetType(),
                JsonOptions,
                cancellationToken);
        }
    }

    internal static ValueTask WriteStatusCodeAsync(HttpContext httpContext)
    {
        var descriptor = PlatformProblemCatalog.ForStatusCode(httpContext.Response.StatusCode);
        var problem = Create(httpContext, descriptor);
        return WriteAsync(httpContext, problem, null, httpContext.RequestAborted);
    }
}
