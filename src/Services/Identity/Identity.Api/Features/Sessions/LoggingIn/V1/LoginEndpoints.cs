using System.Text.Encodings.Web;
using Identity.Api.Infrastructure;
using Identity.Api.Model;
using MediatR;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

internal static partial class LoginEndpoints
{
    private const string LoginPath = "/account/login";
    private const string AuthenticatorCodePath = "/account/login/two-factor";
    private const string RecoveryCodePath = "/account/login/recovery-code";
    private const string InvalidVerificationCode =
        "The verification code is invalid or expired.";

    public static IEndpointRouteBuilder MapLogin(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                LoginPath,
                (HttpContext context, string? returnUrl, IAntiforgery antiforgery) =>
                    RenderLogin(context, returnUrl, null, antiforgery))
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                LoginPath,
                ProcessLoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet(
                AuthenticatorCodePath,
                ShowAuthenticatorCodeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                AuthenticatorCodePath,
                ProcessAuthenticatorCodeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet(
                RecoveryCodePath,
                ShowRecoveryCodeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                RecoveryCodePath,
                ProcessRecoveryCodeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.LoginRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet(
                "/account/reset-password",
                (HttpContext context, Guid userId, string code, IAntiforgery antiforgery) =>
                    RenderPasswordReset(context, userId, code, null, antiforgery))
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapPost(
                "/account/reset-password",
                ProcessPasswordResetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(IdentityServiceExtensions.AccountRateLimitPolicy)
            .ExcludeFromDescription();

        endpoints.MapGet(
                "/account/access-denied",
                () => Results.Content(
                    Page("Access denied", "<h1>Access denied</h1><p>You do not have permission to continue.</p>"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status403Forbidden))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static async Task<IResult> ShowAuthenticatorCodeAsync(
        HttpContext context,
        string? returnUrl,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        return user is not null && user.IsActive
            ? RenderAuthenticatorCode(context, returnUrl, null, antiforgery)
            : Results.LocalRedirect(BuildAccountUrl(LoginPath, returnUrl));
    }

    private static async Task<IResult> ShowRecoveryCodeAsync(
        HttpContext context,
        string? returnUrl,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        return user is not null && user.IsActive
            ? RenderRecoveryCode(context, returnUrl, null, antiforgery)
            : Results.LocalRedirect(BuildAccountUrl(LoginPath, returnUrl));
    }

    private static async Task<IResult> ProcessLoginAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        DummyPasswordVerifier dummyPasswordVerifier,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        var returnUrl = NormalizeLocalReturnUrl(form["returnUrl"].ToString());

        var user = string.IsNullOrWhiteSpace(email)
            ? null
            : await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            dummyPasswordVerifier.Verify(password);
            return RenderLogin(
                context,
                returnUrl,
                "The email or password is invalid.",
                antiforgery);
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            isPersistent: false,
            lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            return Results.LocalRedirect(
                BuildAccountUrl(AuthenticatorCodePath, returnUrl));
        }

        if (!result.Succeeded)
        {
            return RenderLogin(
                context,
                returnUrl,
                "The email or password is invalid.",
                antiforgery);
        }

        await CompleteSuccessfulSignInAsync(
            user,
            signInManager,
            loggerFactory,
            timeProvider);
        return Results.LocalRedirect(returnUrl ?? "/");
    }

    private static async Task<IResult> ProcessAuthenticatorCodeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery))
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = NormalizeLocalReturnUrl(form["returnUrl"].ToString());
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive)
        {
            return RenderAuthenticatorCode(
                context,
                returnUrl,
                InvalidVerificationCode,
                antiforgery);
        }

        var code = form["code"].ToString()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code,
            isPersistent: false,
            rememberClient: false);
        if (!result.Succeeded)
        {
            return RenderAuthenticatorCode(
                context,
                returnUrl,
                InvalidVerificationCode,
                antiforgery);
        }

        await CompleteSuccessfulSignInAsync(
            user,
            signInManager,
            loggerFactory,
            timeProvider);
        return Results.LocalRedirect(returnUrl ?? "/");
    }

    private static async Task<IResult> ProcessRecoveryCodeAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        if (!await IsAntiforgeryRequestValidAsync(context, antiforgery))
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var returnUrl = NormalizeLocalReturnUrl(form["returnUrl"].ToString());
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null || !user.IsActive)
        {
            return RenderRecoveryCode(
                context,
                returnUrl,
                InvalidVerificationCode,
                antiforgery);
        }

        var code = form["code"].ToString()
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(code);
        if (!result.Succeeded)
        {
            return RenderRecoveryCode(
                context,
                returnUrl,
                InvalidVerificationCode,
                antiforgery);
        }

        await CompleteSuccessfulSignInAsync(
            user,
            signInManager,
            loggerFactory,
            timeProvider);
        return Results.LocalRedirect(returnUrl ?? "/");
    }

    private static async Task CompleteSuccessfulSignInAsync(
        ApplicationUser user,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
    {
        var authenticationTime = timeProvider.GetUtcNow();
        var authenticationProperties = new AuthenticationProperties
        {
            AllowRefresh = true,
            IsPersistent = false,
            IssuedUtc = authenticationTime
        };
        OidcAuthenticationState.SetAuthenticationTime(
            authenticationProperties,
            authenticationTime);
        await signInManager.SignInAsync(
            user,
            authenticationProperties,
            authenticationMethod: null);

        var loginLogger = loggerFactory.CreateLogger("Identity.Login");
        LogUserSignedIn(loginLogger, user.Id);
    }

    private static async Task<bool> IsAntiforgeryRequestValidAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Identity user {UserId} signed in")]
    private static partial void LogUserSignedIn(ILogger logger, Guid userId);

    private static async Task<IResult> ProcessPasswordResetAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ISender sender)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        if (!Guid.TryParse(form["userId"], out var userId))
        {
            return Results.BadRequest();
        }

        await sender.Send(
            new Identity.Api.Features.Accounts.RecoveringPassword.V1.ResetPassword(
                userId,
                form["code"].ToString(),
                form["newPassword"].ToString()),
            context.RequestAborted);

        return Results.Content(
            Page(
                "Password changed",
                "<h1>Password changed</h1><p>You can return to the application and sign in.</p>"),
            "text/html; charset=utf-8");
    }

    private static IResult RenderLogin(
        HttpContext context,
        string? returnUrl,
        string? error,
        IAntiforgery antiforgery)
    {
        var encoder = HtmlEncoder.Default;
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException("An antiforgery token could not be created.");
        var body = $"""
            <h1>Sign in</h1>
            {(error is null ? string.Empty : $"<p role=\"alert\">{encoder.Encode(error)}</p>")}
            <form method="post" action="{LoginPath}">
              <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
              <input type="hidden" name="returnUrl" value="{encoder.Encode(NormalizeLocalReturnUrl(returnUrl) ?? string.Empty)}">
              <p><label>Email <input name="email" type="email" autocomplete="username" required maxlength="254"></label></p>
              <p><label>Password <input name="password" type="password" autocomplete="current-password" required maxlength="128"></label></p>
              <button type="submit">Sign in</button>
            </form>
            """;

        return Results.Content(Page("Sign in", body), "text/html; charset=utf-8");
    }

    private static IResult RenderAuthenticatorCode(
        HttpContext context,
        string? returnUrl,
        string? error,
        IAntiforgery antiforgery)
    {
        var encoder = HtmlEncoder.Default;
        var normalizedReturnUrl = NormalizeLocalReturnUrl(returnUrl);
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException("An antiforgery token could not be created.");
        var recoveryCodeUrl = BuildAccountUrl(RecoveryCodePath, normalizedReturnUrl);
        var body = $"""
            <h1>Two-factor verification</h1>
            <p>Enter the code from your authenticator app.</p>
            {(error is null ? string.Empty : $"<p role=\"alert\">{encoder.Encode(error)}</p>")}
            <form method="post" action="{AuthenticatorCodePath}">
              <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
              <input type="hidden" name="returnUrl" value="{encoder.Encode(normalizedReturnUrl ?? string.Empty)}">
              <p><label>Authenticator code <input name="code" type="text" inputmode="numeric" autocomplete="one-time-code" required maxlength="32"></label></p>
              <button type="submit">Verify</button>
            </form>
            <p><a href="{encoder.Encode(recoveryCodeUrl)}">Use a recovery code</a></p>
            """;

        return Results.Content(
            Page("Two-factor verification", body),
            "text/html; charset=utf-8");
    }

    private static IResult RenderRecoveryCode(
        HttpContext context,
        string? returnUrl,
        string? error,
        IAntiforgery antiforgery)
    {
        var encoder = HtmlEncoder.Default;
        var normalizedReturnUrl = NormalizeLocalReturnUrl(returnUrl);
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException("An antiforgery token could not be created.");
        var authenticatorCodeUrl = BuildAccountUrl(
            AuthenticatorCodePath,
            normalizedReturnUrl);
        var body = $"""
            <h1>Recovery-code verification</h1>
            <p>Enter one of your account recovery codes.</p>
            {(error is null ? string.Empty : $"<p role=\"alert\">{encoder.Encode(error)}</p>")}
            <form method="post" action="{RecoveryCodePath}">
              <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
              <input type="hidden" name="returnUrl" value="{encoder.Encode(normalizedReturnUrl ?? string.Empty)}">
              <p><label>Recovery code <input name="code" type="text" autocomplete="one-time-code" autocapitalize="none" spellcheck="false" required maxlength="128"></label></p>
              <button type="submit">Verify</button>
            </form>
            <p><a href="{encoder.Encode(authenticatorCodeUrl)}">Use an authenticator code</a></p>
            """;

        return Results.Content(
            Page("Recovery-code verification", body),
            "text/html; charset=utf-8");
    }

    private static IResult RenderPasswordReset(
        HttpContext context,
        Guid userId,
        string code,
        string? error,
        IAntiforgery antiforgery)
    {
        var encoder = HtmlEncoder.Default;
        var token = antiforgery.GetAndStoreTokens(context).RequestToken ??
            throw new InvalidOperationException("An antiforgery token could not be created.");
        var body = $"""
            <h1>Reset password</h1>
            {(error is null ? string.Empty : $"<p role=\"alert\">{encoder.Encode(error)}</p>")}
            <form method="post" action="/account/reset-password">
              <input type="hidden" name="__RequestVerificationToken" value="{encoder.Encode(token)}">
              <input type="hidden" name="userId" value="{userId:D}">
              <input type="hidden" name="code" value="{encoder.Encode(code)}">
              <p><label>New password <input name="newPassword" type="password" autocomplete="new-password" required minlength="15" maxlength="128"></label></p>
              <button type="submit">Reset password</button>
            </form>
            """;

        return Results.Content(Page("Reset password", body), "text/html; charset=utf-8");
    }

    private static string? NormalizeLocalReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value[0] == '/' &&
               (value.Length == 1 || value[1] is not '/' and not '\\')
            ? value
            : null;
    }

    private static string BuildAccountUrl(string path, string? returnUrl)
    {
        var normalizedReturnUrl = NormalizeLocalReturnUrl(returnUrl);
        return normalizedReturnUrl is null
            ? path
            : path + QueryString.Create("returnUrl", normalizedReturnUrl);
    }

    private static string Page(string title, string body) =>
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{HtmlEncoder.Default.Encode(title)}</title></head><body><main>{body}</main></body></html>";
}
