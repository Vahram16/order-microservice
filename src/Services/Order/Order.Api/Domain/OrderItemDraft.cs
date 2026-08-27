namespace Order.Api.Domain;

public sealed record OrderItemDraft(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string CurrencyCode);
