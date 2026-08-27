namespace Inventory.Api.Features.Inventory.Common;

internal sealed record InventoryResponse(Guid ProductId, int OnHand, int Reserved, int Available, long Version);
