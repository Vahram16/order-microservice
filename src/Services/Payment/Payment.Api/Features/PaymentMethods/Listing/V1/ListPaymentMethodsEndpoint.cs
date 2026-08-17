using System.Security.Claims;
using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;

namespace Payment.Api.Features.PaymentMethods.Listing.V1;

internal static class ListPaymentMethodsEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(
                "/",
                async (
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

                    var result = await sender.Send(
                        new ListPaymentMethodsQuery(
                            identity.Value.Provider,
                            identity.Value.Subject),
                        cancellationToken);

                    return result.Match<IResult>(
                        Results.Ok,
                        error => PaymentHttp.Problem(error, httpContext));
                })
            .WithName("ListPaymentMethodsV1")
            .WithSummary("Lists the authenticated customer's saved payment methods.")
            .RequireAuthorization(
                RolePolicy.For(PaymentAuthorization.Role),
                ScopePolicy.For(PaymentAuthorization.ReadScope))
            .Produces<IReadOnlyList<PaymentMethodResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
}
