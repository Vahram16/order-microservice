using System.Net;
using System.Threading.RateLimiting;
using Identity.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Api.Tests;

public sealed class ProtocolRateLimitingTests
{
    [Theory]
    [InlineData("/connect/authorize", 60)]
    [InlineData("/connect/par", 30)]
    [InlineData("/connect/revocation", 30)]
    [InlineData("/connect/userinfo", 120)]
    [InlineData("/connect/logout", 30)]
    public void GlobalLimiterThrottlesProtocolEndpoints(
        string path,
        int permitLimit)
    {
        using var provider = CreateProvider();
        var limiter = provider
            .GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value
            .GlobalLimiter;

        Assert.NotNull(limiter);
        for (var index = 0; index < permitLimit; index++)
        {
            using var lease = AttemptAcquire(limiter, path);
            Assert.True(lease.IsAcquired);
        }

        using var rejected = AttemptAcquire(limiter, path);
        Assert.False(rejected.IsAcquired);
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/api/v1/profile/me")]
    [InlineData("/health")]
    [InlineData("/connect/par/nested")]
    public void GlobalLimiterDoesNotThrottleOtherPaths(string path)
    {
        using var provider = CreateProvider();
        var limiter = provider
            .GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value
            .GlobalLimiter;

        Assert.NotNull(limiter);
        for (var index = 0; index < 250; index++)
        {
            using var lease = AttemptAcquire(limiter, path);
            Assert.True(lease.IsAcquired);
        }
    }

    private static RateLimitLease AttemptAcquire(
        PartitionedRateLimiter<HttpContext> limiter,
        string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        return limiter.AttemptAcquire(context);
    }

    private static ServiceProvider CreateProvider()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Identity.Api.Tests",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development,
            DisableDefaults = true
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:identity-db"] =
                "Host=localhost;Database=identity_tests;Username=identity;Password=unused",
            ["AuthorizationServer:Issuer"] = "https://identity.example.test/",
            ["AuthorizationServer:Scopes:0:Name"] = "identity.profile.read",
            ["AuthorizationServer:Scopes:0:DisplayName"] = "Read identity profile",
            ["AuthorizationServer:Scopes:0:Resource"] = "identity-api",
            ["IdentityNotifications:Provider"] = "DevelopmentLog",
            ["IdentityNotifications:PublicOrigin"] = "https://identity.example.test/"
        });
        builder.AddIdentityService();

        return builder.Services.BuildServiceProvider(validateScopes: true);
    }
}
