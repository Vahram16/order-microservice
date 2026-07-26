using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.RecoveryCode.V1;

internal static class RecoveryCodeEndpoint
{
    public static async Task<IResult> HandleAsync(
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
