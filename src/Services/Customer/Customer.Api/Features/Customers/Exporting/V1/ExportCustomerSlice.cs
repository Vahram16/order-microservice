using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal static class ExportCustomerSlice
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

internal sealed record ExportCustomerQuery(string Provider, string Subject)
    : IRequest<CustomerExportResponse>;

internal sealed class ExportCustomerQueryHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<ExportCustomerQuery, CustomerExportResponse>
{
    public async Task<CustomerExportResponse> Handle(
        ExportCustomerQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Include(entity => entity.Addresses)
            .SingleOrDefaultAsync(
                entity =>
                    entity.IdentityProvider == request.Provider &&
                    entity.IdentitySubject == request.Subject,
                cancellationToken)
            ?? throw new Domain.CustomerNotFoundException();

        return new CustomerExportResponse(
            timeProvider.GetUtcNow(),
            CustomerMappings.ToResponse(customer));
    }
}
