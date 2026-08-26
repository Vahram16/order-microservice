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
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    if (identity.IsFailure)
                    {
                        return CustomerHttpResults.Problem(identity.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new GetCurrentCustomerQuery(identity.Value.Provider, identity.Value.Subject),
                        cancellationToken);

                    return result.Match<IResult>(
                        customer =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, customer.Version);
                            return Results.Ok(customer);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("GetCurrentCustomer")
            .WithSummary("Gets the customer bound to the current Keycloak subject.")
            .RequireAuthorization(RolePolicy.For(CustomerAuthorization.ReadRole))
            .Produces<CustomerResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
