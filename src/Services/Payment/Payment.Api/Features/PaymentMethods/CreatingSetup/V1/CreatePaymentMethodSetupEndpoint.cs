using System.Security.Claims;
using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal static class CreatePaymentMethodSetupEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost(
                "/setup",
                async (
                    ClaimsPrincipal principal,
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentPaymentIdentity.From(principal);
                    if (identity.IsFailure)
                    {
                        return PaymentHttpResults.Problem(identity.Error, httpContext);
                    }

                    var requestId = PaymentHttpResults.ReadIdempotencyKey(httpContext.Request);
                    if (requestId.IsFailure)
                    {
                        return PaymentHttpResults.Problem(requestId.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new CreatePaymentMethodSetupCommand(identity.Value, requestId.Value),
                        cancellationToken);

                    return result.Match<IResult>(
                        success =>
                        {
                            httpContext.Response.Headers.CacheControl = "no-store";
                            httpContext.Response.Headers.Pragma = "no-cache";
                            return Results.Ok(success);
                        },
                        error => PaymentHttpResults.Problem(error, httpContext));
                })
            .WithName("CreatePaymentMethodSetup")
            .WithSummary("Creates or resumes a future-use payment method setup session.")
            .RequireAuthorization(
                RolePolicy.For(PaymentAuthorization.Role),
                ScopePolicy.For(PaymentAuthorization.WriteScope))
            .Produces<CreatePaymentMethodSetupResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
