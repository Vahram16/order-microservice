using MediatR;

namespace Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1;

internal static class ResetPasswordEndpoint
{
    public static async Task<IResult> HandleAsync(
        ResetPasswordRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ResetPasswordCommand(
                request.UserId,
                request.Code,
                request.NewPassword),
            cancellationToken);

        return Results.NoContent();
    }
}
