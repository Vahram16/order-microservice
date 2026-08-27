using MediatR;
using Microservices.Security;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;

namespace Payment.Api.Features.OrderPayments.GettingAction.V1;

internal static class GetOrderPaymentActionEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/{paymentAttemptId:guid}/action", async (
                Guid paymentAttemptId,
                HttpContext context,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var identity = CurrentPaymentIdentity.From(context.User);
                if (identity.IsFailure)
                {
                    return PaymentHttpResults.Problem(identity.Error, context);
                }

                var result = await sender.Send(
                    new GetOrderPaymentActionQuery(paymentAttemptId, identity.Value.Provider, identity.Value.Subject),
                    cancellationToken);
                return result.Match<IResult>(
                    response => Results.Ok(response),
                    error => PaymentHttpResults.Problem(error, context));
            })
            .WithName("GetOrderPaymentAction")
            .WithSummary("Returns the customer action secret only for an owned payment attempt that currently requires 3-D Secure or equivalent provider action.")
            .RequireAuthorization(RolePolicy.For(PaymentAuthorization.ManageRole))
            .Produces<OrderPaymentActionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
}
