using Microservices.Application;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Persistence;

namespace Payment.Api.Features.PaymentMethods.SettingDefault.V1;

internal sealed class SetDefaultPaymentMethodHandler(
    PaymentDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<SetDefaultPaymentMethodCommand, Result<PaymentMethodResponse>>
{
    public async Task<Result<PaymentMethodResponse>> Handle(
        SetDefaultPaymentMethodCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.PaymentCustomers.FindByIdentityAsync(
            command.Identity.Provider,
            command.Identity.Subject,
            cancellationToken);
        if (customer is null)
        {
            return PaymentApplicationErrors.CustomerNotSynchronized;
        }

        var methods = await dbContext.PaymentMethods
            .Where(method =>
                method.PaymentCustomerId == customer.Id &&
                method.Status == PaymentMethodStatus.Active)
            .ToListAsync(cancellationToken);
        var target = methods.SingleOrDefault(method => method.Id == command.PaymentMethodId);
        if (target is null)
        {
            return PaymentApplicationErrors.PaymentMethodNotFound;
        }

        if (target.IsDefault)
        {
            return Result.Success(PaymentMethodMappings.ToResponse(target));
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var current = methods.SingleOrDefault(method => method.IsDefault);
        if (current is not null)
        {
            current.ClearDefault(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var makeDefault = target.MakeDefault(now);
        if (makeDefault.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return makeDefault.Error;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(PaymentMethodMappings.ToResponse(target));
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(
                PaymentDatabaseConstraints.DefaultPaymentMethod))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return PaymentApplicationErrors.ConcurrencyConflict;
        }
    }
}
