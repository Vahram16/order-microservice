using FluentValidation;
using Identity.Api.Features.Accounts;
using Identity.Api.Features.Authorization;
using Identity.Api.Features.Presentation;
using Identity.Api.Features.Profile;
using MediatR;
using Microservices.Application;

namespace Identity.Api.Infrastructure;

internal static class IdentityEndpointExtensions
{
    public static IServiceCollection AddIdentityApplication(
        this IServiceCollection services,
        IConfiguration applicationConfiguration)
    {
        services.AddValidatorsFromAssemblyContaining<Program>();
        services.AddMediatR(mediator =>
        {
            mediator.RegisterServicesFromAssemblyContaining<Program>();
            mediator.AddOpenBehavior(typeof(ValidationBehavior<,>));
            mediator.LicenseKey = applicationConfiguration["Licensing:MediatR"];
        });
        services.AddSingleton<IdentityPageRenderer>();

        return services;
    }

    public static IEndpointRouteBuilder MapIdentityApplication(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthorizationEndpoints();
        endpoints.MapAccountEndpoints();
        endpoints.MapProfileEndpoints();
        return endpoints;
    }
}
