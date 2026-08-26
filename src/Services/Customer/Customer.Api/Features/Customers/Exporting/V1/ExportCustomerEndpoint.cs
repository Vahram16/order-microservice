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
                        new ExportCustomerQuery(identity.Value.Provider, identity.Value.Subject),
                        cancellationToken);

                    return result.Match<IResult>(
                        export =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, export.Customer.Version);
                            httpContext.Response.Headers.ContentDisposition =
                                "attachment; filename=customer-data.json";
                            return Results.Ok(export);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("ExportCurrentCustomer")
            .WithSummary("Exports all Customer-service-owned data for the authenticated customer.")
            .RequireAuthorization(RolePolicy.For(CustomerAuthorization.ExportRole))
            .Produces<CustomerExportResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
}
