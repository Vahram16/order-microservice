using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure.Stripe;
using Payment.Api.Persistence;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed class CreatePaymentMethodSetupHandler(
    PaymentDbContext dbContext,
    IStripeGateway stripeGateway,
    TimeProvider timeProvider)
    : ICommandHandler<CreatePaymentMethodSetupCommand, Result<CreatePaymentMethodSetupResult>>
{
    public async Task<Result<CreatePaymentMethodSetupResult>> Handle(
        CreatePaymentMethodSetupCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.PaymentCustomers.SingleOrDefaultAsync(
            candidate => candidate.IdentityProvider == command.IdentityProvider &&
                         candidate.IdentitySubject == command.IdentitySubject,
            cancellationToken);
        if (customer is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var stripeCustomerId = customer.StripeCustomerId;
        if (stripeCustomerId is null)
        {
            stripeCustomerId = await stripeGateway.CreateCustomerAsync(
                customer.CustomerId,
                StripeIdempotencyKeys.Customer(customer.CustomerId),
                cancellationToken);

            customer.AssignStripeCustomer(stripeCustomerId, timeProvider.GetUtcNow());
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.ChangeTracker.Clear();
                customer = await dbContext.PaymentCustomers.SingleAsync(
                    candidate => candidate.CustomerId == customer.CustomerId,
                    cancellationToken);
                if (!string.Equals(customer.StripeCustomerId, stripeCustomerId, StringComparison.Ordinal))
                {
                    throw;
                }
            }
        }

        var setupIntent = await stripeGateway.CreateSetupIntentAsync(
            stripeCustomerId,
            customer.CustomerId,
            command.RequestId,
            command.MakeDefault,
            StripeIdempotencyKeys.SetupIntent(customer.CustomerId, command.RequestId),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(setupIntent.ClientSecret))
        {
            throw new InvalidOperationException("Stripe returned a SetupIntent without a client secret.");
        }

        return Result.Success(new CreatePaymentMethodSetupResult(
            command.RequestId,
            setupIntent.Id,
            setupIntent.ClientSecret));
    }
}
