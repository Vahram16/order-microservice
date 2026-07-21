using MediatR;

namespace Identity.Api.Features.Sessions.LoggingIn.V1;

internal static class LoginEndpoint
{
    public static async Task<IResult> PasswordAsync(
        LoginRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var outcome = await sender.Send(
            new LoginCommand(request.Email, request.Password),
            cancellationToken);

        return outcome switch
        {
            LoginOutcome.Succeeded => Results.NoContent(),
            LoginOutcome.RequiresTwoFactor => Results.Json(
                new LoginResponse("two_factor"),
                statusCode: StatusCodes.Status202Accepted),
            _ => AuthenticationFailed()
        };
    }

    public static async Task<IResult> AuthenticatorAsync(
        AuthenticatorCodeRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var outcome = await sender.Send(
            new AuthenticatorCodeCommand(request.Code),
            cancellationToken);

        return outcome == LoginOutcome.Succeeded
            ? Results.NoContent()
            : AuthenticationFailed();
    }

    public static async Task<IResult> RecoveryCodeAsync(
        RecoveryCodeRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var outcome = await sender.Send(
            new RecoveryCodeCommand(request.Code),
            cancellationToken);

        return outcome == LoginOutcome.Succeeded
            ? Results.NoContent()
            : AuthenticationFailed();
    }

    private static IResult AuthenticationFailed() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed",
            detail: "The submitted credentials or verification code are invalid.");
}
