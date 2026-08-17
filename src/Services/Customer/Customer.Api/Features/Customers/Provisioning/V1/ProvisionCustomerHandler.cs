using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microservices.Application.Messaging;
using Microservices.Contracts.Customers.V1;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.Provisioning.V1;

internal sealed class ProvisionCustomerHandler(
    CustomerDbContext dbContext,
    IIntegrationEventPublisher eventPublisher,
    TimeProvider timeProvider)
    : ICommandHandler<ProvisionCustomerCommand, Result<ProvisionCustomerResult>>
{
    public async Task<Result<ProvisionCustomerResult>> Handle(
        ProvisionCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Customers.FindByIdentityAsync(
            command.Identity.Provider,
            command.Identity.Subject,
            cancellationToken);
        if (existing is not null)
        {
            await PublishIdentityAsync(existing, timeProvider.GetUtcNow(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(new ProvisionCustomerResult(
                CustomerMappings.ToResponse(existing),
                false));
        }

        var now = timeProvider.GetUtcNow();
        var registration = Domain.Customer.Register(
            command.Identity.Provider,
            command.Identity.Subject,
            command.Identity.GivenName,
            command.Identity.FamilyName,
            command.Identity.Email,
            now);
        if (registration.IsFailure)
        {
            return CustomerApplicationErrors.InvalidIdentityClaims;
        }

        var customer = registration.Value;
        dbContext.Customers.Add(customer);
        dbContext.AddAuditEntry(
            customer,
            command.Identity.Subject,
            Domain.CustomerAuditActions.Provisioned,
            now);
        await PublishIdentityAsync(customer, now, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(new ProvisionCustomerResult(
                CustomerMappings.ToResponse(customer),
                true));
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(CustomerDatabaseConstraints.Identity))
        {
            dbContext.ChangeTracker.Clear();
            existing = await dbContext.Customers.FindByIdentityAsync(
                command.Identity.Provider,
                command.Identity.Subject,
                cancellationToken);

            if (existing is null)
            {
                throw;
            }

            await PublishIdentityAsync(existing, timeProvider.GetUtcNow(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success(new ProvisionCustomerResult(
                CustomerMappings.ToResponse(existing),
                false));
        }
    }

    private Task PublishIdentityAsync(
        Domain.Customer customer,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken) =>
        eventPublisher.PublishAsync(
            new CustomerIdentitySynchronized(
                customer.Id,
                customer.IdentityProvider,
                customer.IdentitySubject,
                occurredAtUtc),
            cancellationToken: cancellationToken);
}
