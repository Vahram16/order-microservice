using Inventory.Api.Domain;
using Inventory.Api.Features.Inventory.Common;
using Inventory.Api.Persistence;
using Microservices.Application;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Features.Inventory.SettingStock.V1;

internal sealed class SetInventoryStockHandler(InventoryDbContext dbContext, TimeProvider timeProvider)
    : ICommandHandler<SetInventoryStockCommand, Result<InventoryResponse>>
{
    public async Task<Result<InventoryResponse>> Handle(SetInventoryStockCommand command, CancellationToken cancellationToken)
    {
        var item = await dbContext.InventoryItems.SingleOrDefaultAsync(value => value.ProductId == command.ProductId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (item is null)
        {
            if (command.ExpectedVersion is not null)
            {
                return InventoryApplicationErrors.NotFound;
            }

            var creation = InventoryItem.Create(command.ProductId, command.OnHand, now);
            if (creation.IsFailure)
            {
                return creation.Error;
            }

            item = creation.Value;
            dbContext.InventoryItems.Add(item);
        }
        else
        {
            if (command.ExpectedVersion is null)
            {
                return InventoryApplicationErrors.PreconditionRequired;
            }

            var expected = item.EnsureExpectedVersion(command.ExpectedVersion.Value);
            if (expected.IsFailure)
            {
                return expected.Error;
            }

            var update = item.SetOnHand(command.OnHand, now);
            if (update.IsFailure)
            {
                return update.Error;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return InventoryErrors.VersionMismatch;
        }

        return Result.Success(new InventoryResponse(item.ProductId, item.OnHand, item.Reserved, item.Available, item.Version));
    }
}
