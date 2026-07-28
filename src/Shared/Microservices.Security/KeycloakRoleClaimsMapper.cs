using System.Security.Claims;
using System.Text.Json;

namespace Microservices.Security;

internal static class KeycloakRoleClaimsMapper
{
    private const string ResourceAccessClaim = "resource_access";
    private const string RealmAccessClaim = "realm_access";
    private const string RolesProperty = "roles";

    public static void MapRoles(
        ClaimsPrincipal principal,
        ApiSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(options);

        var identity = principal.Identities
            .OfType<ClaimsIdentity>()
            .FirstOrDefault(candidate => candidate.IsAuthenticated);

        if (identity is null)
        {
            return;
        }

        var roleClientId = string.IsNullOrWhiteSpace(options.RoleClientId)
            ? options.Audience
            : options.RoleClientId;

        var roles = new HashSet<string>(StringComparer.Ordinal);
        AddClientRoles(principal, roleClientId, roles);

        if (options.MapRealmRoles)
        {
            AddRealmRoles(principal, roles);
        }

        foreach (var role in roles)
        {
            if (!identity.HasClaim(SecurityClaimTypes.Role, role))
            {
                identity.AddClaim(new Claim(SecurityClaimTypes.Role, role));
            }
        }
    }

    private static void AddClientRoles(
        ClaimsPrincipal principal,
        string clientId,
        HashSet<string> roles)
    {
        foreach (var claim in FindAuthenticatedClaims(principal, ResourceAccessClaim))
        {
            if (!TryParseObject(claim.Value, out var document))
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty(clientId, out var clientAccess) ||
                    clientAccess.ValueKind != JsonValueKind.Object ||
                    !clientAccess.TryGetProperty(RolesProperty, out var roleValues))
                {
                    continue;
                }

                AddRoles(roleValues, roles);
            }
        }
    }

    private static void AddRealmRoles(
        ClaimsPrincipal principal,
        HashSet<string> roles)
    {
        foreach (var claim in FindAuthenticatedClaims(principal, RealmAccessClaim))
        {
            if (!TryParseObject(claim.Value, out var document))
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.TryGetProperty(
                        RolesProperty,
                        out var roleValues))
                {
                    AddRoles(roleValues, roles);
                }
            }
        }
    }

    private static IEnumerable<Claim> FindAuthenticatedClaims(
        ClaimsPrincipal principal,
        string claimType) =>
        principal.Identities
            .Where(identity => identity.IsAuthenticated)
            .SelectMany(identity => identity.FindAll(claimType));

    private static void AddRoles(
        JsonElement roleValues,
        HashSet<string> roles)
    {
        if (roleValues.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var roleValue in roleValues.EnumerateArray())
        {
            if (roleValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var role = roleValue.GetString();
            if (!string.IsNullOrWhiteSpace(role))
            {
                roles.Add(role);
            }
        }
    }

    private static bool TryParseObject(
        string value,
        out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
        }
        catch (JsonException)
        {
            // A malformed optional role claim grants no roles.
        }

        document = null!;
        return false;
    }
}
