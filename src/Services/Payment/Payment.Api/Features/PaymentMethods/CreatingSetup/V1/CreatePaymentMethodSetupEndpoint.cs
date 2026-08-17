using System.Security.Claims;
using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal static class CreatePaymentMethodSetupEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost(
                "/setup",
                async (
                    CreatePaymentMethodSetupRequest request,
                    ClaimsPrincipal principal,
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentPaymentIdentity.From(principal);
                    if (identity.IsFailure)
                    {
                        return PaymentHttp.Problem(identity.Error, httpContext);
                    }

                    var idempotencyKey = PaymentHttp.ReadIdempotencyKey(httpContext.Request.Headers);
                    if (idempotencyKey.IsFailure)
                    {
                        return PaymentHttp.Problem(idempotencyKey.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new CreatePaymentMethodSetupCommand(
                            identity.Value.Provider,
                            identity.Value.Subject,
                            idempotencyKey.Value,
                            request.MakeDefault ?? true),
                        cancellationToken);

                    return result.Match<IResult>(
                        success => Results.Ok(new
                        {
                            success.RequestId,
                            success.SetupIntentId,
                            success.ClientSecret
                        }),
                        error => PaymentHttp.Problem(error, httpContext));
                })
            .WithName("CreatePaymentMethodSetupV1")
            .WithSummary("Creates an idempotent Stripe SetupIntent for future off-session use.")
            .RequireAuthorization(
                RolePolicy.For(PaymentAuthorization.Role),
                ScopePolicy.For(PaymentAuthorization.WriteScope))
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
