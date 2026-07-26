using MediatR;

namespace Identity.Api.Features.Accounts.EmailConfirmation.Resend.V1;

internal static class ResendEmailConfirmationEndpoint
{
    public static async Task<IResult> HandleAsync(
        ResendEmailConfirmationRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ResendEmailConfirmationCommand(request.Email),
            cancellationToken);
        return Results.Accepted();
    }
}
