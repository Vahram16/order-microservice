using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;

namespace Notifications.Api.Security;

internal sealed class InternalApiKeyValidator(
    IOptions<NotificationsIngressOptions> options)
{
    private readonly byte[] _expectedHash = SHA256.HashData(
        Encoding.UTF8.GetBytes(options.Value.ApiKey ?? string.Empty));

    public bool IsAuthorized(string authorization)
    {
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        if (token.Length is < 32 or > 4096)
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(_expectedHash, suppliedHash);
    }
}

internal sealed class InternalNotificationIngressMiddleware(
    RequestDelegate next,
    InternalApiKeyValidator apiKeyValidator)
{
    private static readonly PathString IngressPath =
        new("/internal/v1/notifications/identity");

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            context.Request.Path.Equals(IngressPath) &&
            !apiKeyValidator.IsAuthorized(
                context.Request.Headers.Authorization.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.CacheControl = "no-store";
            return;
        }

        await next(context);
    }
}

internal static class InternalNotificationIngressMiddlewareExtensions
{
    public static IApplicationBuilder UseInternalNotificationIngressAuthentication(
        this IApplicationBuilder application) =>
        application.UseMiddleware<InternalNotificationIngressMiddleware>();
}
