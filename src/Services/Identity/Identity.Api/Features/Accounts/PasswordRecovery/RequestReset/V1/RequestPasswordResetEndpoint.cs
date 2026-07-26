using MediatR;

namespace Identity.Api.Features.Accounts.PasswordRecovery.RequestReset.V1;

internal static class RequestPasswordResetEndpoint
{
    public static async Task<IResult> HandleAsync(
        RequestPasswordResetRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RequestPasswordResetCommand(request.Email),
            cancellationToken);

        return Results.Accepted();
    }
}
