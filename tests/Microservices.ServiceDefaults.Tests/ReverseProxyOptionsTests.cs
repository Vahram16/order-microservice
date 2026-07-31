using Microservices.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Microservices.ServiceDefaults.Tests;

public sealed class ReverseProxyOptionsTests
{
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void EnabledProxyRejectsCatchAllKnownNetworks(string cidr)
    {
        var result = new ReverseProxyOptionsValidator().Validate(
            null,
            new ReverseProxyOptions
            {
                Enabled = true,
                KnownNetworks = [cidr],
                AllowedHosts = ["api.example.test"]
            });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void EnabledProxyRejectsUnspecifiedKnownProxies(string address)
    {
        var result = new ReverseProxyOptionsValidator().Validate(
            null,
            new ReverseProxyOptions
            {
                Enabled = true,
                KnownProxies = [address],
                AllowedHosts = ["api.example.test"]
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void EnabledProxyAllowsExplicitBoundedKnownNetwork()
    {
        var result = new ReverseProxyOptionsValidator().Validate(
            null,
            new ReverseProxyOptions
            {
                Enabled = true,
                KnownNetworks = ["10.0.0.0/8"],
                AllowedHosts = ["api.example.test"]
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SetupHandlesNullOptionalProxyArrayAfterSuccessfulValidation()
    {
        var configured = new ReverseProxyOptions
        {
            Enabled = true,
            KnownProxies = null!,
            KnownNetworks = ["10.0.0.0/8"],
            AllowedHosts = [" api.example.test "]
        };
        var validation = new ReverseProxyOptionsValidator().Validate(null, configured);
        var forwarded = new ForwardedHeadersOptions();

        ReverseProxyOptionsSetup.Configure(forwarded, configured);

        Assert.True(validation.Succeeded);
        Assert.Empty(forwarded.KnownProxies);
        Assert.Single(forwarded.KnownIPNetworks);
        Assert.Equal("api.example.test", Assert.Single(forwarded.AllowedHosts));
    }
}
