using Customer.Api.Features.Customers.Common;
using Customer.Api.Persistence;
using Microservices.Application;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal sealed class UpdateCustomerDetailsHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerDetailsCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        UpdateCustomerDetailsCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.GetRequiredByIdentityAsync(
            command.Provider,
            command.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(command.ExpectedVersion);

        var now = timeProvider.GetUtcNow();
        customer.UpdateDetails(
            command.FirstName,
            command.LastName,
            command.Email,
            command.PhoneNumber,
            now);
        dbContext.AddAuditEntry(
            customer,
            command.Subject,
            Domain.CustomerAuditActions.DetailsUpdated,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
