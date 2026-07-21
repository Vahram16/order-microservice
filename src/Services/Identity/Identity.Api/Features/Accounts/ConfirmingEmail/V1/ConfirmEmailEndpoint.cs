using MediatR;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal static class ConfirmEmailEndpoint
{
    public static async Task<IResult> HandleAsync(
        ConfirmEmailRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ConfirmEmailCommand(request.UserId, request.Code),
            cancellationToken);

        return Results.NoContent();
    }
}
