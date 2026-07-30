using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal static class ProvisionCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut(
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
                        new ProvisionCustomerCommand(identity.Value),
                        cancellationToken);

                    return result.Match<IResult>(
                        success =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, success.Customer.Version);
                            return success.Created
                                ? Results.Created("/api/v1/customers/me", success.Customer)
                                : Results.Ok(success.Customer);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("ProvisionCurrentCustomer")
            .WithSummary("Idempotently provisions the customer bound to the current Keycloak subject.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.UpdateScope))
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
