using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Microservices.Security;

public static class ScopePolicy
{
    public const string Prefix = "scope:";

    public static string For(string scope)
    {
        ScopeRequirement.ValidateScope(scope);
        return Prefix + scope;
    }
}

public static class RolePolicy
{
    public const string Prefix = "role:";

    public static string For(string role)
    {
        ValidateRole(role);
        return Prefix + role;
    }

    internal static void ValidateRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (role.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "The role must not contain whitespace.",
                nameof(role));
        }
    }
}

public sealed class ScopeRequirement : IAuthorizationRequirement
{
    public ScopeRequirement(string scope)
    {
        ValidateScope(scope);
        Scope = scope;
    }

    public string Scope { get; }

    internal static void ValidateScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (scope.Any(character =>
                character != '\u0021' &&
                (character < '\u0023' || character > '\u005B') &&
                (character < '\u005D' || character > '\u007E')))
        {
            throw new ArgumentException(
                "The scope must be a valid OAuth 2.0 scope-token.",
                nameof(scope));
        }
    }
}

internal sealed class ScopeAuthorizationHandler
    : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ScopeRequirement requirement)
    {
        if (context.User.Identities
            .Where(identity => identity.IsAuthenticated)
            .SelectMany(identity => identity.FindAll(SecurityClaimTypes.Scope))
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries))
            .Contains(requirement.Scope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal sealed class ScopeAuthorizationPolicyProvider(
    IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ScopePolicy.Prefix, StringComparison.Ordinal))
        {
            return CreateScopePolicy(policyName[ScopePolicy.Prefix.Length..]);
        }

        if (policyName.StartsWith(RolePolicy.Prefix, StringComparison.Ordinal))
        {
            return CreateRolePolicy(policyName[RolePolicy.Prefix.Length..]);
        }

        return base.GetPolicyAsync(policyName);
    }

    private static Task<AuthorizationPolicy?> CreateScopePolicy(string scope)
    {
        try
        {
            var requirement = new ScopeRequirement(scope);
            var policy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(requirement)
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        catch (ArgumentException)
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }
    }

    private static Task<AuthorizationPolicy?> CreateRolePolicy(string role)
    {
        try
        {
            RolePolicy.ValidateRole(role);
            var policy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(role)
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        catch (ArgumentException)
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }
    }
}
