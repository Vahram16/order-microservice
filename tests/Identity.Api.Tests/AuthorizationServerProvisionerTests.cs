using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Identity.Api.Configuration;
using Identity.Api.Provisioning;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Identity.Api.Tests;

public sealed class AuthorizationServerProvisionerTests
{
    private static readonly IReadOnlySet<string> ConfiguredIdentifiers =
        new HashSet<string>(["current"], StringComparer.Ordinal);

    [Fact]
    public async Task ProvisioningMarksCreatesAndPrunesOnlyStaleOwnedEntries()
    {
        var retiredApplication = new object();
        var operatorApplication = new object();
        var retiredScope = new object();
        var operatorScope = new object();
        var deletedApplications = new List<object>();
        var deletedScopes = new List<object>();
        OpenIddictApplicationDescriptor? createdApplication = null;
        OpenIddictScopeDescriptor? createdScope = null;

        var applicationManager = CreateProxy<IOpenIddictApplicationManager>((method, arguments) =>
        {
            switch (method.Name)
            {
                case nameof(IOpenIddictApplicationManager.FindByClientIdAsync):
                    return new ValueTask<object?>((object?)null);
                case nameof(IOpenIddictApplicationManager.CreateAsync)
                    when arguments is not null &&
                    arguments[0] is OpenIddictApplicationDescriptor descriptor:
                    createdApplication = descriptor;
                    return new ValueTask<object>(new object());
                case nameof(IOpenIddictApplicationManager.ListAsync):
                    return Enumerate(retiredApplication, operatorApplication);
                case nameof(IOpenIddictApplicationManager.GetPropertiesAsync):
                    return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                        ReferenceEquals(arguments?[0], retiredApplication)
                            ? ManagedImmutableProperties()
                            : ImmutableDictionary<string, JsonElement>.Empty);
                case nameof(IOpenIddictApplicationManager.GetClientIdAsync):
                    return new ValueTask<string?>(
                        ReferenceEquals(arguments?[0], retiredApplication)
                            ? "retired-client"
                            : "operator-client");
                case nameof(IOpenIddictApplicationManager.DeleteAsync):
                    deletedApplications.Add(arguments![0]!);
                    return ValueTask.CompletedTask;
                default:
                    throw UnexpectedInvocation(method);
            }
        });

        var scopeManager = CreateProxy<IOpenIddictScopeManager>((method, arguments) =>
        {
            switch (method.Name)
            {
                case nameof(IOpenIddictScopeManager.FindByNameAsync):
                    return new ValueTask<object?>((object?)null);
                case nameof(IOpenIddictScopeManager.CreateAsync)
                    when arguments is not null &&
                    arguments[0] is OpenIddictScopeDescriptor descriptor:
                    createdScope = descriptor;
                    return new ValueTask<object>(new object());
                case nameof(IOpenIddictScopeManager.ListAsync):
                    return Enumerate(retiredScope, operatorScope);
                case nameof(IOpenIddictScopeManager.GetPropertiesAsync):
                    return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                        ReferenceEquals(arguments?[0], retiredScope)
                            ? ManagedImmutableProperties()
                            : ImmutableDictionary<string, JsonElement>.Empty);
                case nameof(IOpenIddictScopeManager.GetNameAsync):
                    return new ValueTask<string?>(
                        ReferenceEquals(arguments?[0], retiredScope)
                            ? "retired.scope"
                            : "operator.scope");
                case nameof(IOpenIddictScopeManager.DeleteAsync):
                    deletedScopes.Add(arguments![0]!);
                    return ValueTask.CompletedTask;
                default:
                    throw UnexpectedInvocation(method);
            }
        });

        var options = new AuthorizationServerOptions
        {
            Scopes =
            [
                new AuthorizationScopeOptions
                {
                    Name = "current.scope",
                    DisplayName = "Current scope",
                    Resource = "current-api"
                }
            ],
            Clients =
            [
                new AuthorizationClientOptions
                {
                    ClientId = "current-client",
                    DisplayName = "Current client",
                    Profile = AuthorizationClientProfile.Public,
                    Scopes = ["current.scope"]
                }
            ]
        };
        var provisioner = new AuthorizationServerProvisioner(
            applicationManager,
            scopeManager,
            Options.Create(options),
            new TestHostEnvironment(),
            NullLogger<AuthorizationServerProvisioner>.Instance);

        await provisioner.ProvisionAsync(CancellationToken.None);

        Assert.NotNull(createdApplication);
        Assert.True(AuthorizationServerProvisioner.IsManaged(createdApplication.Properties));
        Assert.DoesNotContain(
            OpenIddictConstants.Permissions.Endpoints.PushedAuthorization,
            createdApplication.Permissions);
        Assert.NotNull(createdScope);
        Assert.True(AuthorizationServerProvisioner.IsManaged(createdScope.Properties));
        Assert.Equal([retiredApplication], deletedApplications);
        Assert.Equal([retiredScope], deletedScopes);
    }

    [Fact]
    public void RemovesOnlyOwnedEntryMissingFromManifest()
    {
        var properties = ManagedProperties();

        Assert.True(AuthorizationServerProvisioner.ShouldRemove(
            properties,
            "retired",
            ConfiguredIdentifiers));
    }

    [Fact]
    public void PreservesOwnedEntryStillPresentInManifest()
    {
        var properties = ManagedProperties();

        Assert.False(AuthorizationServerProvisioner.ShouldRemove(
            properties,
            "current",
            ConfiguredIdentifiers));
    }

    [Fact]
    public void PreservesUnownedEntryMissingFromManifest()
    {
        var properties = new Dictionary<string, JsonElement>();

        Assert.False(AuthorizationServerProvisioner.ShouldRemove(
            properties,
            "operator-managed",
            ConfiguredIdentifiers));
    }

    [Theory]
    [InlineData("another-provisioner")]
    [InlineData("")]
    public void PreservesEntryWithDifferentOwner(string owner)
    {
        var properties = new Dictionary<string, JsonElement>
        {
            [AuthorizationServerProvisioner.OwnershipProperty] =
                JsonSerializer.SerializeToElement(owner)
        };

        Assert.False(AuthorizationServerProvisioner.ShouldRemove(
            properties,
            "retired",
            ConfiguredIdentifiers));
    }

    [Fact]
    public void RemovesOwnedEntryWhoseIdentifierCannotBeRead()
    {
        Assert.True(AuthorizationServerProvisioner.ShouldRemove(
            ManagedProperties(),
            identifier: null,
            ConfiguredIdentifiers));
    }

    private static Dictionary<string, JsonElement> ManagedProperties() =>
        new Dictionary<string, JsonElement>
        {
            [AuthorizationServerProvisioner.OwnershipProperty] =
                JsonSerializer.SerializeToElement(AuthorizationServerProvisioner.OwnershipValue)
        };

    private static ImmutableDictionary<string, JsonElement> ManagedImmutableProperties() =>
        ImmutableDictionary<string, JsonElement>.Empty.Add(
            AuthorizationServerProvisioner.OwnershipProperty,
            JsonSerializer.SerializeToElement(AuthorizationServerProvisioner.OwnershipValue));

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> invoke)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestDispatchProxy<T>>();
        ((TestDispatchProxy<T>)(object)proxy).InvokeMember = invoke;
        return proxy;
    }

    private static async IAsyncEnumerable<object> Enumerate(params object[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static InvalidOperationException UnexpectedInvocation(MethodInfo method) =>
        new($"Unexpected invocation of {method.DeclaringType?.Name}.{method.Name}.");

    public class TestDispatchProxy<T> : DispatchProxy
        where T : class
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMember { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            InvokeMember(
                targetMethod ?? throw new InvalidOperationException("Proxy method is required."),
                args);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Identity.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
