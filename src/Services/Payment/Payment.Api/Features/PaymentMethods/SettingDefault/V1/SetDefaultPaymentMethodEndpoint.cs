using System.Security.Claims;
using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;
namespace Payment.Api.Features.PaymentMethods.SettingDefault.V1;

internal static class SetDefaultPaymentMethodEndpoint
{
    public static void Map(IEndpointRouteBuilder group) => group.MapPut("/{paymentMethodId:guid}/default", async (Guid paymentMethodId, ClaimsPrincipal principal, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
    { if (paymentMethodId == Guid.Empty) return PaymentHttpResults.Problem(PaymentApplicationErrors.PaymentMethodNotFound, httpContext); var identity = CurrentPaymentIdentity.From(principal); if (identity.IsFailure) return PaymentHttpResults.Problem(identity.Error, httpContext); var result = await sender.Send(new SetDefaultPaymentMethodCommand(identity.Value, paymentMethodId), cancellationToken); return result.Match<IResult>(method => Results.Ok(method), error => PaymentHttpResults.Problem(error, httpContext)); })
    .WithName("SetDefaultPaymentMethod").WithSummary("Sets one saved payment method as the authenticated customer's default.").RequireAuthorization(RolePolicy.For(PaymentAuthorization.ManageRole)).Produces<PaymentMethodResponse>(200).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
}
