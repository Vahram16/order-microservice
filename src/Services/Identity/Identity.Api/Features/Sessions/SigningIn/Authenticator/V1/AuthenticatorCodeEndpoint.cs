using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.Authenticator.V1;

internal static class AuthenticatorCodeEndpoint
{
    public static async Task<IResult> HandleAsync(
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

    private static IResult AuthenticationFailed() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed",
            detail: "The submitted credentials or verification code are invalid.");
}
