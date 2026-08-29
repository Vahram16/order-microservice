namespace Inventory.Api.Domain;

public sealed class InventoryItem
{
    private InventoryItem() { }
    private InventoryItem(Guid productId, int onHand, DateTimeOffset now)
    {
        ProductId = productId;
        OnHand = onHand;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid ProductId { get; private set; }
    public int OnHand { get; private set; }
    public int Reserved { get; private set; }
    public int Available => OnHand - Reserved;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Result<InventoryItem> Create(Guid productId, int onHand, DateTimeOffset now) => productId == Guid.Empty || onHand < 0 ? InventoryErrors.InvalidQuantity : Result.Success(new InventoryItem(productId, onHand, now));
    public Result EnsureExpectedVersion(long expectedVersion) => expectedVersion > 0 && Version == expectedVersion ? Result.Success() : InventoryErrors.VersionMismatch;

    public Result SetOnHand(int onHand, DateTimeOffset now)
    {
        if (onHand < 0) return InventoryErrors.InvalidQuantity;
        if (onHand < Reserved) return InventoryErrors.ReservedStockConflict;
        if (OnHand == onHand) return Result.Success();
        OnHand = onHand;
        Touch(now);
        return Result.Success();
    }

    public Result Reserve(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0) return InventoryErrors.InvalidQuantity;
        if (Available < quantity) return InventoryErrors.InsufficientStock;
        Reserved += quantity;
        Touch(now);
        return Result.Success();
    }

    public Result Release(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || Reserved < quantity) return InventoryErrors.InvalidReservationState;
        Reserved -= quantity;
        Touch(now);
        return Result.Success();
    }

    public Result Commit(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || Reserved < quantity || OnHand < quantity) return InventoryErrors.InvalidReservationState;
        Reserved -= quantity;
        OnHand -= quantity;
        Touch(now);
        return Result.Success();
    }

    public Result RestoreCommitted(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || OnHand > int.MaxValue - quantity) return InventoryErrors.InvalidReservationState;
        OnHand += quantity;
        Touch(now);
        return Result.Success();
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
    }
}
