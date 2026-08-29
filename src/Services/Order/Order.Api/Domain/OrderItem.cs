using Microservices.Primitives;

namespace Order.Api.Domain;

public sealed class OrderItem
{
    private OrderItem() { }
    private OrderItem(Guid id, Guid orderId, Guid productId, string sku, string productName, int quantity, decimal unitPrice, decimal lineTotal)
    {
        Id = id; OrderId = orderId; ProductId = productId; Sku = sku; ProductName = productName; Quantity = quantity; UnitPrice = unitPrice; LineTotal = lineTotal;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = null!;
    public string ProductName { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineTotal { get; private set; }

    internal static Result<OrderItem> Create(Guid orderId, OrderItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var sku = draft.Sku?.Trim();
        var name = draft.ProductName?.Trim();
        if (orderId == Guid.Empty || draft.ProductId == Guid.Empty || draft.Quantity <= 0 ||
            string.IsNullOrWhiteSpace(sku) || sku.Length > 64 || string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
            draft.UnitPrice < 0m || !CurrencyAmount.TryNormalizeCurrencyCode(draft.CurrencyCode, out var currency) ||
            !CurrencyAmount.HasValidScale(draft.UnitPrice, currency) || draft.UnitPrice > decimal.MaxValue / draft.Quantity)
            return OrderErrors.InvalidItem;
        return Result.Success(new OrderItem(Guid.NewGuid(), orderId, draft.ProductId, sku, name, draft.Quantity, draft.UnitPrice, draft.UnitPrice * draft.Quantity));
    }
}
