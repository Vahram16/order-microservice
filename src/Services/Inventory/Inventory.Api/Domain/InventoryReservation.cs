namespace Inventory.Api.Domain;

public sealed class InventoryReservation
{
    private readonly List<InventoryReservationLine> _lines = [];

    private InventoryReservation() { }

    private InventoryReservation(Guid id, Guid orderId, InventoryReservationStatus status, string? reasonCode, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        Status = status;
        ReasonCode = reasonCode;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public InventoryReservationStatus Status { get; private set; }
    public string? ReasonCode { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<InventoryReservationLine> Lines => _lines.AsReadOnly();

    public static Result<InventoryReservation> CreateActive(
        Guid orderId,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (orderId == Guid.Empty || lines.Count == 0 || expiresAt <= now ||
            lines.Any(line => line.ProductId == Guid.Empty || line.Quantity <= 0) ||
            lines.Select(line => line.ProductId).Distinct().Count() != lines.Count)
        {
            return InventoryErrors.InvalidReservation;
        }

        var reservation = new InventoryReservation(Guid.NewGuid(), orderId, InventoryReservationStatus.Active, null, expiresAt, now);
        foreach (var line in lines)
        {
            reservation._lines.Add(new InventoryReservationLine(reservation.Id, line.ProductId, line.Quantity));
        }

        return Result.Success(reservation);
    }

    public static InventoryReservation CreateRejected(Guid orderId, string reasonCode, DateTimeOffset expiresAt, DateTimeOffset now) =>
        new(Guid.NewGuid(), orderId, InventoryReservationStatus.Rejected, reasonCode, expiresAt, now);

    public Result Release(DateTimeOffset now)
    {
        if (Status is InventoryReservationStatus.Released or InventoryReservationStatus.Expired)
        {
            return Result.Success();
        }

        if (Status != InventoryReservationStatus.Active)
        {
            return InventoryErrors.InvalidReservationState;
        }

        Status = InventoryReservationStatus.Released;
        Touch(now);
        return Result.Success();
    }

    public Result Commit(DateTimeOffset now)
    {
        if (Status == InventoryReservationStatus.Committed)
        {
            return Result.Success();
        }

        if (Status != InventoryReservationStatus.Active)
        {
            return InventoryErrors.InvalidReservationState;
        }

        Status = InventoryReservationStatus.Committed;
        Touch(now);
        return Result.Success();
    }

    public Result Expire(DateTimeOffset now)
    {
        if (Status == InventoryReservationStatus.Expired)
        {
            return Result.Success();
        }

        if (Status != InventoryReservationStatus.Active)
        {
            return InventoryErrors.InvalidReservationState;
        }

        if (now < ExpiresAt)
        {
            return InventoryErrors.InvalidReservationState;
        }

        Status = InventoryReservationStatus.Expired;
        Touch(now);
        return Result.Success();
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
    }
}
