using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Identity.Api.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Identity.Api.Features.Authorization;

internal sealed class LogoutInteractionProtector
{
    private const string Purpose = "Identity.Api.LogoutInteraction.v1";
    private const int MaximumTokenLength = 16_384;

    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public LogoutInteractionProtector(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<IdentityInteractionOptions> options,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _timeProvider = timeProvider;
        _lifetime = options.Value.InteractionTokenLifetime;
    }

    public string Protect(string completionUri)
    {
        if (!IsLogoutCompletionUri(completionUri))
        {
            throw new InvalidOperationException(
                "The logout completion URI must target the local end-session endpoint.");
        }

        var payload = new LogoutInteractionPayload(
            completionUri,
            _timeProvider.GetUtcNow().Add(_lifetime));
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public bool IsValid(string? token, string completionUri)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            token.Length > MaximumTokenLength ||
            !IsLogoutCompletionUri(completionUri))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<LogoutInteractionPayload>(json);
            if (payload is null ||
                payload.ExpiresAtUtc <= _timeProvider.GetUtcNow() ||
                !IsLogoutCompletionUri(payload.CompletionUri))
            {
                return false;
            }

            return FixedTimeEquals(payload.CompletionUri, completionUri);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsLogoutCompletionUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith('/', StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Contains('\r') ||
            value.Contains('\n'))
        {
            return false;
        }

        var queryIndex = value.IndexOf('?');
        var path = queryIndex >= 0 ? value[..queryIndex] : value;
        return string.Equals(
            path,
            "/connect/logout",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private sealed record LogoutInteractionPayload(
        string CompletionUri,
        DateTimeOffset ExpiresAtUtc);
}
