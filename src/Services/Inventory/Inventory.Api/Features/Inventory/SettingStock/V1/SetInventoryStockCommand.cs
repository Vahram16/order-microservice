using Inventory.Api.Features.Inventory.Common;
using Microservices.Application;

namespace Inventory.Api.Features.Inventory.SettingStock.V1;

internal sealed record SetInventoryStockCommand(Guid ProductId, int OnHand, long? ExpectedVersion)
    : ICommand<Result<InventoryResponse>>;
