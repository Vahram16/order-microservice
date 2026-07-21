using MediatR;

namespace Identity.Api.Features.Accounts.Registering.V1;

internal static class RegisterAccountEndpoint
{
    public static async Task<IResult> HandleAsync(
        RegisterAccountRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RegisterAccountCommand(
                request.Email,
                request.Password,
                request.DisplayName),
            cancellationToken);

        return Results.Accepted();
    }
}
