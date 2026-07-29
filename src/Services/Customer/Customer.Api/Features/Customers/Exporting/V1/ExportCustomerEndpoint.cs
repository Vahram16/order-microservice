using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal static class ExportCustomerEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet(
                "/export",
                async (
                    ClaimsPrincipal principal,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var export = await sender.Send(
                        new ExportCustomerQuery(identity.Provider, identity.Subject),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, export.Customer.Version);
                    response.Headers.ContentDisposition =
                        "attachment; filename=customer-data.json";
                    return Results.Ok(export);
                })
            .WithName("ExportCurrentCustomer")
            .WithSummary("Exports all Customer-service-owned data for the authenticated customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.ExportScope))
            .Produces<CustomerExportResponse>()
            .Produces(StatusCodes.Status404NotFound);
}
