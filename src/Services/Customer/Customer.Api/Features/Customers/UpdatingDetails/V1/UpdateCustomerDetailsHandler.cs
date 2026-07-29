using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal sealed class UpdateCustomerDetailsHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerDetailsCommand, Result<CustomerResponse>>
{
    public async Task<Result<CustomerResponse>> Handle(
        UpdateCustomerDetailsCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.FindByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        if (customer is null)
        {
            return CustomerApplicationErrors.CustomerNotFound;
        }

        var version = customer.EnsureExpectedVersion(command.ExpectedVersion);
        if (version.IsFailure)
        {
            return version.Error;
        }

        var now = timeProvider.GetUtcNow();
        var update = customer.UpdateDetails(
            command.FirstName,
            command.LastName,
            command.Email,
            command.PhoneNumber,
            now);
        if (update.IsFailure)
        {
            return update.Error;
        }

        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            CustomerAuditActions.DetailsUpdated,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CustomerErrors.VersionMismatch;
        }

        return Result.Success(CustomerMappings.ToResponse(customer));
    }
}
