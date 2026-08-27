namespace Inventory.Api.Domain;

public sealed class InventoryReservationLine
{
    private InventoryReservationLine() { }

    internal InventoryReservationLine(Guid reservationId, Guid productId, int quantity)
    {
        Id = Guid.NewGuid();
        ReservationId = reservationId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}
