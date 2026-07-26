using Identity.Api.Features.Sessions.SigningIn;
using MediatR;

namespace Identity.Api.Features.Sessions.SigningIn.Password.V1;

internal static class PasswordLoginEndpoint
{
    public static async Task<IResult> HandleAsync(
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

    private static IResult AuthenticationFailed() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed",
            detail: "The submitted credentials or verification code are invalid.");
}
