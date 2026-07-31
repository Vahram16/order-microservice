using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Microservices.ServiceDefaults;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; init; }

    public int ForwardLimit { get; init; } = 1;

    public string[] KnownProxies { get; init; } = [];

    public string[] KnownNetworks { get; init; } = [];

    public string[] AllowedHosts { get; init; } = [];
}

internal sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var knownProxies = options.KnownProxies ?? [];
        var knownNetworks = options.KnownNetworks ?? [];
        var allowedHosts = options.AllowedHosts ?? [];

        if (options.ForwardLimit is < 1 or > 5)
        {
            failures.Add("'ReverseProxy:ForwardLimit' must be between 1 and 5.");
        }

        if (knownProxies.Length == 0 && knownNetworks.Length == 0)
        {
            failures.Add(
                "An enabled reverse proxy requires at least one explicit known proxy or network.");
        }

        foreach (var value in knownProxies)
        {
            if (!IPAddress.TryParse(value?.Trim(), out var address))
            {
                failures.Add($"Reverse proxy address '{value}' is not a valid IP address.");
            }
            else if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
            {
                failures.Add(
                    $"Reverse proxy address '{value}' cannot trust every source address.");
            }
        }

        foreach (var value in knownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(value?.Trim(), out var network))
            {
                failures.Add($"Reverse proxy network '{value}' is not valid CIDR notation.");
            }
            else if (network.PrefixLength == 0)
            {
                failures.Add(
                    $"Reverse proxy network '{value}' cannot trust every source address.");
            }
        }

        if (allowedHosts.Length == 0 ||
            allowedHosts.Any(host =>
                host?.Trim() is null or "" or "*" or "0.0.0.0" or "[::]"))
        {
            failures.Add(
                "An enabled reverse proxy requires explicit 'ReverseProxy:AllowedHosts' values.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal static class ReverseProxyOptionsSetup
{
    public static void Configure(
        ForwardedHeadersOptions forwarded,
        ReverseProxyOptions configured)
    {
        if (!configured.Enabled)
        {
            return;
        }

        forwarded.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedHost |
            ForwardedHeaders.XForwardedProto;
        forwarded.ForwardLimit = configured.ForwardLimit;
        forwarded.RequireHeaderSymmetry = true;
        forwarded.KnownProxies.Clear();
        forwarded.KnownIPNetworks.Clear();
        forwarded.AllowedHosts.Clear();

        foreach (var value in configured.KnownProxies ?? [])
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(value.Trim()));
        }

        foreach (var value in configured.KnownNetworks ?? [])
        {
            forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(value.Trim()));
        }

        foreach (var value in configured.AllowedHosts ?? [])
        {
            forwarded.AllowedHosts.Add(value.Trim());
        }
    }
}
