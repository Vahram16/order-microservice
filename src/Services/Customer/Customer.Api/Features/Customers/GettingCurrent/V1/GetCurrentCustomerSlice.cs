using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.GettingCurrent.V1;

internal static class GetCurrentCustomerSlice
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

internal sealed record GetCurrentCustomerQuery(string Provider, string Subject)
    : IRequest<CustomerResponse?>;

internal sealed class GetCurrentCustomerQueryHandler(CustomerDbContext dbContext)
    : IRequestHandler<GetCurrentCustomerQuery, CustomerResponse?>
{
    public async Task<CustomerResponse?> Handle(
        GetCurrentCustomerQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Include(entity => entity.Addresses)
            .SingleOrDefaultAsync(
                entity =>
                    entity.IdentityProvider == request.Provider &&
                    entity.IdentitySubject == request.Subject,
                cancellationToken);

        return customer is null ? null : CustomerMappings.ToResponse(customer);
    }
}
