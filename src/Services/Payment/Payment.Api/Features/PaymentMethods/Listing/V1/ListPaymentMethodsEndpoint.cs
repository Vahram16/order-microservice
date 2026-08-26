using System.Security.Claims;
using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;
namespace Payment.Api.Features.PaymentMethods.Listing.V1;

internal static class ListPaymentMethodsEndpoint
{
    public static void Map(IEndpointRouteBuilder group) => group.MapGet("/", async (ClaimsPrincipal principal, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
    { var identity = CurrentPaymentIdentity.From(principal); if (identity.IsFailure) return PaymentHttpResults.Problem(identity.Error, httpContext); var result = await sender.Send(new ListPaymentMethodsQuery(identity.Value), cancellationToken); return result.Match<IResult>(methods => Results.Ok(methods), error => PaymentHttpResults.Problem(error, httpContext)); })
    .WithName("ListPaymentMethods").WithSummary("Lists the authenticated customer's saved payment methods.").RequireAuthorization(RolePolicy.For(PaymentAuthorization.ReadRole)).Produces<IReadOnlyList<PaymentMethodResponse>>(200).ProducesProblem(401).ProducesProblem(403);
}
