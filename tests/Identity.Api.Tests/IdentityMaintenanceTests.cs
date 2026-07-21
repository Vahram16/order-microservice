using Identity.Api.Configuration;
using Identity.Api.Infrastructure;
using Identity.Api.Maintenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Api.Tests;

public sealed class IdentityMaintenanceTests
{
    [Fact]
    public void DefaultMaintenanceOptionsAreValid()
    {
        var result = new IdentityMaintenanceOptionsValidator().Validate(
            name: null,
            new IdentityMaintenanceOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void UnsafeMaintenanceIntervalsAreRejected()
    {
        var validator = new IdentityMaintenanceOptionsValidator();
        var result = validator.Validate(
            name: null,
            new IdentityMaintenanceOptions
            {
                PruneInterval = TimeSpan.FromMinutes(1),
                MinimumAge = TimeSpan.FromMinutes(30),
                FailureRetryInterval = TimeSpan.FromSeconds(1)
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(IdentityMaintenanceOptions.PruneInterval),
                StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(IdentityMaintenanceOptions.MinimumAge),
                StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure =>
            failure.Contains(nameof(IdentityMaintenanceOptions.FailureRetryInterval),
                StringComparison.Ordinal));
    }

    [Fact]
    public void MaintenanceOptionsAreBoundValidatedAndWorkerIsRegistered()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["IdentityMaintenance:PruneInterval"] = "12:00:00",
            ["IdentityMaintenance:MinimumAge"] = "2.00:00:00",
            ["IdentityMaintenance:FailureRetryInterval"] = "00:02:00"
        });
        builder.AddIdentityService();

        using var provider = builder.Services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<IdentityMaintenanceOptions>>().Value;

        Assert.Equal(TimeSpan.FromHours(12), options.PruneInterval);
        Assert.Equal(TimeSpan.FromDays(2), options.MinimumAge);
        Assert.Equal(TimeSpan.FromMinutes(2), options.FailureRetryInterval);
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is OpenIddictMaintenanceService);
    }

    [Fact]
    public async Task PruningAlwaysRemovesTokensBeforeAuthorizations()
    {
        var pruner = new RecordingPruner();
        var operation = new OpenIddictPruningOperation(pruner);
        var threshold = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        var result = await operation.ExecuteAsync(threshold, CancellationToken.None);

        Assert.Equal(["tokens", "authorizations"], pruner.Calls);
        Assert.All(pruner.Thresholds, value => Assert.Equal(threshold, value));
        Assert.Equal(17, result.Tokens);
        Assert.Equal(5, result.Authorizations);
    }

    private static HostApplicationBuilder CreateBuilder(
        IDictionary<string, string?>? additionalConfiguration = null)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Identity.Api.Tests",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
            DisableDefaults = true
        });
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:identity-db"] =
                "Host=localhost;Database=identity_tests;Username=identity;Password=unused",
            ["AuthorizationServer:Issuer"] = "https://identity.example.test/",
            ["AuthorizationServer:Scopes:0:Name"] = "identity.profile.read",
            ["AuthorizationServer:Scopes:0:DisplayName"] = "Read identity profile",
            ["AuthorizationServer:Scopes:0:Resource"] = "identity-api",
            ["IdentityNotifications:Provider"] = "DevelopmentLog",
            ["IdentityNotifications:PublicOrigin"] = "https://identity.example.test/"
        };

        if (additionalConfiguration is not null)
        {
            foreach (var pair in additionalConfiguration)
            {
                configuration[pair.Key] = pair.Value;
            }
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        return builder;
    }

    private sealed class RecordingPruner : IOpenIddictPruner
    {
        public List<string> Calls { get; } = [];

        public List<DateTimeOffset> Thresholds { get; } = [];

        public ValueTask<long> PruneTokensAsync(
            DateTimeOffset threshold,
            CancellationToken cancellationToken)
        {
            Calls.Add("tokens");
            Thresholds.Add(threshold);
            return ValueTask.FromResult(17L);
        }

        public ValueTask<long> PruneAuthorizationsAsync(
            DateTimeOffset threshold,
            CancellationToken cancellationToken)
        {
            Calls.Add("authorizations");
            Thresholds.Add(threshold);
            return ValueTask.FromResult(5L);
        }
    }
}
