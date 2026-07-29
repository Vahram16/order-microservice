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
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new ProvisionCustomerCommand(CurrentIdentity.From(principal)),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, result.Customer.Version);
                    return result.Created
                        ? Results.Created("/api/v1/customers/me", result.Customer)
                        : Results.Ok(result.Customer);
                })
            .WithName("ProvisionCurrentCustomer")
            .WithSummary("Idempotently provisions the customer bound to the current Keycloak subject.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.UpdateScope))
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
}
