using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.ServiceDefaults.ProblemDetails;

public static class MicroserviceProblemDetailsExtensions
{
    public static IServiceCollection AddMicroserviceProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
                context.ProblemDetails.Extensions.TryAdd(
                    "traceId",
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
            };
        });
        services.AddExceptionHandler<ValidationExceptionHandler>();
        services.AddExceptionHandler<UnhandledExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseMicroserviceProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages(async context =>
            await PlatformProblemDetailsWriter.WriteStatusCodeAsync(context.HttpContext));
        return app;
    }

    public static IEndpointRouteBuilder MapMicroserviceErrorCatalog(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/errors/v1/platform/{code}",
                IResult (string code) =>
                {
                    if (!PlatformProblemCatalog.TryResolve(code, out var descriptor))
                    {
                        return Results.NotFound();
                    }

                    return Results.Ok(new
                    {
                        type = descriptor.Type,
                        descriptor.Code,
                        descriptor.Title,
                        descriptor.Status,
                        description = descriptor.Description,
                        descriptor.Retryable
                    });
                })
            .WithName("GetPlatformErrorDescriptionV1")
            .WithSummary("Describes a stable version 1 platform Problem Details type.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        return endpoints;
    }
}
