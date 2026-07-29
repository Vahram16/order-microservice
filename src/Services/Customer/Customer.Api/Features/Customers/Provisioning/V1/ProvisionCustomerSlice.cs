using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal static class ProvisionCustomerSlice
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

internal sealed record ProvisionCustomerCommand(CurrentIdentity Identity)
    : IRequest<ProvisionCustomerResult>;

internal sealed record ProvisionCustomerResult(CustomerResponse Customer, bool Created);

internal sealed class ProvisionCustomerCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<ProvisionCustomerCommand, ProvisionCustomerResult>
{
    public async Task<ProvisionCustomerResult> Handle(
        ProvisionCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await CustomerPersistence.FindAsync(
            dbContext,
            request.Identity.Provider,
            request.Identity.Subject,
            cancellationToken);
        if (existing is not null)
        {
            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }

        var now = timeProvider.GetUtcNow();
        var customer = Domain.Customer.Register(
            request.Identity.Provider,
            request.Identity.Subject,
            request.Identity.GivenName,
            request.Identity.FamilyName,
            request.Identity.Email,
            now);

        dbContext.Customers.Add(customer);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Identity.Subject,
            Domain.CustomerAuditActions.Provisioned,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ProvisionCustomerResult(CustomerMappings.ToResponse(customer), true);
        }
        catch (DbUpdateException exception) when (
            CustomerPersistence.IsUniqueConstraintViolation(
                exception,
                CustomerConstraintNames.Identity))
        {
            dbContext.ChangeTracker.Clear();
            existing = await CustomerPersistence.FindAsync(
                dbContext,
                request.Identity.Provider,
                request.Identity.Subject,
                cancellationToken);

            if (existing is null)
            {
                throw;
            }

            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }
    }
}
