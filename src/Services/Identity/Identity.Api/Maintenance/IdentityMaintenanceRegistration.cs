using Identity.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Identity.Api.Maintenance;

internal static class IdentityMaintenanceRegistration
{
    public static void Add(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<IdentityMaintenanceOptions>,
            IdentityMaintenanceOptionsValidator>();
        services.AddOptions<IdentityMaintenanceOptions>()
            .Bind(configuration.GetSection(IdentityMaintenanceOptions.SectionName))
            .ValidateOnStart();

        services.AddScoped<IOpenIddictPruner, OpenIddictPruner>();
        services.AddScoped<OpenIddictPruningOperation>();
        services.AddHostedService<OpenIddictMaintenanceService>();
    }
}
