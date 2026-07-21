using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Identity.Api.Infrastructure;

internal static class OidcAuthenticationState
{
    private const string AuthenticationTimeProperty = "identity.authentication_time";
    private const string ReauthenticationCookie = "__Host-Identity.OidcReauthentication";
    private const string ReauthenticationProtectorPurpose =
        "Identity.Api.OidcReauthentication.v1";
    private static readonly TimeSpan ReauthenticationLifetime = TimeSpan.FromMinutes(5);

    public static void SetAuthenticationTime(
        AuthenticationProperties properties,
        DateTimeOffset authenticationTime)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[AuthenticationTimeProperty] =
            authenticationTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset? GetAuthenticationTime(
        AuthenticationProperties? properties)
    {
        if (properties is null ||
            !properties.Items.TryGetValue(AuthenticationTimeProperty, out var value) ||
            !long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static void IssueReauthenticationMarker(
        HttpContext context,
        IDataProtectionProvider dataProtectionProvider,
        string requestBinding,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestBinding);

        var expires = now.Add(ReauthenticationLifetime);
        var protector = dataProtectionProvider
            .CreateProtector(ReauthenticationProtectorPurpose)
            .ToTimeLimitedDataProtector();
        var protectedPayload = protector.Protect(
            CreateRequestBinding(requestBinding),
            expires);

        context.Response.Cookies.Append(
            ReauthenticationCookie,
            WebEncoders.Base64UrlEncode(protectedPayload),
            CreateCookieOptions(expires));
    }

    public static bool ConsumeReauthenticationMarker(
        HttpContext context,
        IDataProtectionProvider dataProtectionProvider,
        string requestBinding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestBinding);

        if (!context.Request.Cookies.TryGetValue(
                ReauthenticationCookie,
                out var protectedValue))
        {
            return false;
        }

        try
        {
            var protector = dataProtectionProvider
                .CreateProtector(ReauthenticationProtectorPurpose)
                .ToTimeLimitedDataProtector();
            var payload = protector.Unprotect(
                WebEncoders.Base64UrlDecode(protectedValue),
                out _);
            var expected = CreateRequestBinding(requestBinding);
            var matches = payload.Length == expected.Length &&
                          CryptographicOperations.FixedTimeEquals(payload, expected);
            if (matches)
            {
                DeleteReauthenticationMarker(context);
            }

            return matches;
        }
        catch (CryptographicException)
        {
            DeleteReauthenticationMarker(context);
            return false;
        }
        catch (FormatException)
        {
            DeleteReauthenticationMarker(context);
            return false;
        }
    }

    private static byte[] CreateRequestBinding(string requestBinding) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(requestBinding));

    private static void DeleteReauthenticationMarker(HttpContext context) =>
        context.Response.Cookies.Delete(
            ReauthenticationCookie,
            CreateCookieOptions(expires: null));

    private static CookieOptions CreateCookieOptions(DateTimeOffset? expires) =>
        new()
        {
            Expires = expires,
            HttpOnly = true,
            IsEssential = true,
            MaxAge = expires is null ? null : ReauthenticationLifetime,
            Path = "/",
            SameSite = SameSiteMode.Lax,
            Secure = true
        };
}
