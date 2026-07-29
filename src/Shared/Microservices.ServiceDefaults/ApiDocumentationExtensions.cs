using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Microsoft.Extensions.Hosting;

public sealed record ApiDocumentationOAuthOptions(
    string ClientId,
    string RedirectUri,
    IReadOnlyDictionary<string, string> Scopes);

public static class ApiDocumentationExtensions
{
    private const string DocumentName = "v1";
    private const string BearerScheme = "Bearer";
    private const string OAuthScheme = "OAuth2";
    private const string SecurityAuthorityConfigurationKey = "Security:Authority";
    private const string DeveloperDocumentationContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' https:; " +
        "style-src 'self' 'unsafe-inline' https:; img-src 'self' data: https:; " +
        "font-src 'self' data:; connect-src 'self' http: https:; " +
        "worker-src 'self' blob:; frame-ancestors 'none'; base-uri 'none'; " +
        "form-action 'self'";

    private static readonly ApiDocumentationOAuthOptions DefaultOAuth = new(
        "scalar-dev",
        "https://localhost:7040/scalar/v1",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openid"] = "Authenticate the user.",
            ["profile"] = "Read the user's basic profile.",
            ["email"] = "Read the user's email address.",
            ["orders.read"] = "Read orders allowed by application ownership rules.",
            ["orders.create"] = "Create orders.",
            ["orders.cancel"] = "Cancel orders allowed by application rules."
        });

    public static WebApplicationBuilder AddApiDocumentation(
        this WebApplicationBuilder builder,
        string title) =>
        builder.AddApiDocumentation(title, DefaultOAuth);

    public static WebApplicationBuilder AddApiDocumentation(
        this WebApplicationBuilder builder,
        string title,
        ApiDocumentationOAuthOptions oauth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ValidateOAuthOptions(oauth);

        var authority = builder.Configuration[SecurityAuthorityConfigurationKey];
        var authorizationUrl = GetKeycloakEndpoint(authority, "auth");
        var tokenUrl = GetKeycloakEndpoint(authority, "token");
        builder.Services.AddSingleton(oauth);

        builder.Services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, context, _) =>
            {
                document.Info.Title = title;
                document.Info.Version = context.DocumentName;
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer(new BearerSecuritySchemeTransformer());
            options.AddDocumentTransformer(
                new OAuthSecuritySchemeTransformer(authorizationUrl, tokenUrl, oauth.Scopes));
            options.AddOperationTransformer(new BearerSecurityRequirementTransformer());
        });

        return builder;
    }

    public static WebApplication MapApiDocumentation(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        var oauth = app.Services.GetRequiredService<ApiDocumentationOAuthOptions>();
        app.MapOpenApi()
            .AddEndpointFilter(AddDeveloperDocumentationHeadersAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();
        app.MapScalarApiReference(options => options
            .AddPreferredSecuritySchemes(OAuthScheme)
            .AddAuthorizationCodeFlow(OAuthScheme, flow =>
            {
                flow.ClientId = oauth.ClientId;
                flow.RedirectUri = oauth.RedirectUri;
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = oauth.Scopes.Keys.ToArray();
            }))
            .AddEndpointFilter(AddDeveloperDocumentationHeadersAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private static void ValidateOAuthOptions(ApiDocumentationOAuthOptions oauth)
    {
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentException.ThrowIfNullOrWhiteSpace(oauth.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(oauth.RedirectUri);
        ArgumentNullException.ThrowIfNull(oauth.Scopes);

        if (!Uri.TryCreate(oauth.RedirectUri, UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "The development API documentation redirect URI must be an absolute HTTPS URI.");
        }

        if (oauth.Scopes.Count == 0 || oauth.Scopes.Keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "At least one non-empty API documentation OAuth scope is required.");
        }
    }

    private static Uri GetKeycloakEndpoint(string? authority, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer))
        {
            throw new InvalidOperationException(
                $"{SecurityAuthorityConfigurationKey} must be an absolute URI.");
        }

        return new Uri(
            $"{issuer.AbsoluteUri.TrimEnd('/')}/protocol/openid-connect/{endpoint}",
            UriKind.Absolute);
    }

    private sealed class OAuthSecuritySchemeTransformer(
        Uri authorizationUrl,
        Uri tokenUrl,
        IReadOnlyDictionary<string, string> scopes) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[OAuthScheme] =
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Description = "Sign in through Keycloak.",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = authorizationUrl,
                            TokenUrl = tokenUrl,
                            Scopes = scopes.ToDictionary(
                                scope => scope.Key,
                                scope => scope.Value,
                                StringComparer.Ordinal)
                        }
                    }
                };

            return Task.CompletedTask;
        }
    }

    private static async ValueTask<object?> AddDeveloperDocumentationHeadersAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        context.HttpContext.Response.Headers["Content-Security-Policy"] =
            DeveloperDocumentationContentSecurityPolicy;

        return await next(context);
    }

    private sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??=
                new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerScheme] =
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter a bearer access token issued by the authorization server."
                };

            return Task.CompletedTask;
        }
    }

    private sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAllowAnonymous>().Any())
            {
                return Task.CompletedTask;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(OAuthScheme, context.Document)] = []
            });
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = []
            });

            return Task.CompletedTask;
        }
    }
}
