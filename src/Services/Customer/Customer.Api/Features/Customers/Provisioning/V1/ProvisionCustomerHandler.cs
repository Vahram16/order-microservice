using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed class ProvisionCustomerHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<ProvisionCustomerCommand, ProvisionCustomerResult>
{
    public async Task<ProvisionCustomerResult> Handle(
        ProvisionCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await CustomerPersistence.FindAsync(
            dbContext,
            command.Identity.Provider,
            command.Identity.Subject,
            cancellationToken);
        if (existing is not null)
        {
            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }

        var now = timeProvider.GetUtcNow();
        var customer = Domain.Customer.Register(
            command.Identity.Provider,
            command.Identity.Subject,
            command.Identity.GivenName,
            command.Identity.FamilyName,
            command.Identity.Email,
            now);

        dbContext.Customers.Add(customer);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            command.Identity.Subject,
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
                command.Identity.Provider,
                command.Identity.Subject,
                cancellationToken);

            if (existing is null)
            {
                throw;
            }

            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }
    }
}
