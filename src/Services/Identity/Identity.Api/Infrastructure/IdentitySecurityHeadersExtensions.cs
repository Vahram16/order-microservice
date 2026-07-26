namespace Identity.Api.Infrastructure;

internal static class IdentitySecurityHeadersExtensions
{
    public static IApplicationBuilder UseIdentitySecurityHeaders(
        this IApplicationBuilder application) =>
        application.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                context.Response.Headers.TryAdd("Referrer-Policy", "no-referrer");
                context.Response.Headers.TryAdd(
                    "Permissions-Policy",
                    "camera=(), microphone=(), geolocation=()");
                context.Response.Headers.TryAdd(
                    "Content-Security-Policy",
                    "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'");

                if (context.Request.Path.StartsWithSegments("/connect") ||
                    context.Request.Path.StartsWithSegments("/account"))
                {
                    context.Response.Headers.CacheControl = "no-store, no-cache";
                    context.Response.Headers.Pragma = "no-cache";
                }

                return Task.CompletedTask;
            });

            await next();
        });
}
