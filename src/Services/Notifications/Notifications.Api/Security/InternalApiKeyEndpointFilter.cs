using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;

namespace Notifications.Api.Security;

internal sealed class InternalApiKeyEndpointFilter(
    IOptions<NotificationsIngressOptions> options)
    : IEndpointFilter
{
    private readonly byte[] _expectedHash = SHA256.HashData(
        Encoding.UTF8.GetBytes(options.Value.ApiKey ?? string.Empty));

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Unauthorized();
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        if (token.Length is < 32 or > 4096)
        {
            return Results.Unauthorized();
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return CryptographicOperations.FixedTimeEquals(_expectedHash, suppliedHash)
            ? await next(context)
            : Results.Unauthorized();
    }
}
