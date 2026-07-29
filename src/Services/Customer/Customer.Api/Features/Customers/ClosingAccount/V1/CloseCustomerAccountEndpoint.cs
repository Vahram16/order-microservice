using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal static class CloseCustomerAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete(
                "/",
                async (
                    ClaimsPrincipal principal,
                    HttpRequest request,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new CloseCustomerAccountCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(request)),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("CloseCurrentCustomerAccount")
            .WithSummary("Anonymizes Customer-owned PII, removes saved addresses, and deactivates the customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.DeleteScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}
