using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.GettingCurrent.V1;

internal static class GetCurrentCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(
                "/",
                async (
                    ClaimsPrincipal principal,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new GetCurrentCustomerQuery(identity.Provider, identity.Subject),
                        cancellationToken);
                    if (customer is null)
                    {
                        return Results.NotFound();
                    }

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("GetCurrentCustomer")
            .WithSummary("Gets the customer bound to the current Keycloak subject.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.ReadScope))
            .Produces<CustomerResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
}
