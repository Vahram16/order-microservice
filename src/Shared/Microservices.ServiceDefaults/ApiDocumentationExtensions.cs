using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Microsoft.Extensions.Hosting;

public static class ApiDocumentationExtensions
{
    private const string DocumentName = "v1";
    private const string BearerScheme = "Bearer";
    private const string OAuthScheme = "OAuth2";
    private const string DeveloperDocumentationContentSecurityPolicy =
        "default-src 'self'; script-src 'self' 'unsafe-inline' https:; " +
        "style-src 'self' 'unsafe-inline' https:; img-src 'self' data: https:; " +
        "font-src 'self' data:; connect-src 'self' http: https:; " +
        "worker-src 'self' blob:; frame-ancestors 'none'; base-uri 'none'; " +
        "form-action 'self'";

    public static WebApplicationBuilder AddApiDocumentation(
        this WebApplicationBuilder builder,
        string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        builder.Services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, context, _) =>
            {
                document.Info.Title = title;
                document.Info.Version = context.DocumentName;
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer(new BearerSecuritySchemeTransformer());
            options.AddDocumentTransformer(new OAuthSecuritySchemeTransformer());
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

        app.MapOpenApi()
            .AddEndpointFilter(AddDeveloperDocumentationHeadersAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();
        app.MapScalarApiReference(options => options
            .AddPreferredSecuritySchemes(OAuthScheme)
            .AddAuthorizationCodeFlow(OAuthScheme, flow =>
            {
                flow.ClientId = "scalar-dev";
                flow.RedirectUri = "https://localhost:7100/scalar/v1";
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes =
                [
                    "openid",
                    "profile",
                    "email",
                    "offline_access",
                    "flight.read",
                    "booking.read",
                    "booking.create",
                    "booking.cancel",
                    "passenger.self.read",
                    "passenger.self.update",
                    "identity.profile.read"
                ];
            }))
            .AddEndpointFilter(AddDeveloperDocumentationHeadersAsync)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }

    private sealed class OAuthSecuritySchemeTransformer : IOpenApiDocumentTransformer
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
                    Description = "Sign in through the Identity service.",
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri(
                                "https://localhost:7100/connect/authorize"),
                            TokenUrl = new Uri(
                                "https://localhost:7100/connect/token"),
                            Scopes = new Dictionary<string, string>
                            {
                                ["openid"] = "Authenticate the user.",
                                ["profile"] = "Read the user's basic profile.",
                                ["email"] = "Read the user's email address.",
                                ["offline_access"] = "Request a refresh token.",
                                ["flight.read"] = "Search and view flights.",
                                ["booking.read"] = "View the user's bookings.",
                                ["booking.create"] = "Create bookings.",
                                ["booking.cancel"] = "Cancel bookings.",
                                ["passenger.self.read"] = "Read the user's passenger profile.",
                                ["passenger.self.update"] = "Update the user's passenger profile.",
                                ["identity.profile.read"] = "Read the user's identity profile."
                            }
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
