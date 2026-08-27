namespace Order.Api.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order() { }

    private Order(
        Guid id,
        Guid customerId,
        Guid paymentMethodId,
        string currencyCode,
        decimal total,
        ShippingAddress shippingAddress,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Id = id;
        CustomerId = customerId;
        PaymentMethodId = paymentMethodId;
        CurrencyCode = currencyCode;
        Total = total;
        ShippingAddress = shippingAddress;
        Status = OrderStatus.AwaitingInventory;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public string CurrencyCode { get; private set; } = null!;
    public decimal Total { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid? InventoryReservationId { get; private set; }
    public Guid? PaymentAttemptId { get; private set; }
    public ShippingAddress ShippingAddress { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? TerminalReasonCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    public bool IsTerminal => Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.Expired;

    public static Result<Order> Place(
        Guid id,
        Guid customerId,
        Guid paymentMethodId,
        IReadOnlyCollection<OrderItemDraft> itemDrafts,
        ShippingAddressData shippingAddress,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(itemDrafts);
        ArgumentNullException.ThrowIfNull(shippingAddress);

        if (id == Guid.Empty || customerId == Guid.Empty || paymentMethodId == Guid.Empty)
        {
            return OrderErrors.InvalidItem;
        }

        if (itemDrafts.Count == 0)
        {
            return OrderErrors.EmptyOrder;
        }

        if (expiresAt <= now)
        {
            return OrderErrors.InvalidDeadline;
        }

        if (itemDrafts.Select(item => item.ProductId).Distinct().Count() != itemDrafts.Count)
        {
            return OrderErrors.DuplicateProduct;
        }

        var currency = itemDrafts.First().CurrencyCode?.Trim().ToUpperInvariant();
        if (currency is null || currency.Length != 3 ||
            currency.Any(character => character is not (>= 'A' and <= 'Z')) ||
            itemDrafts.Any(item => !string.Equals(
                item.CurrencyCode?.Trim(),
                currency,
                StringComparison.OrdinalIgnoreCase)))
        {
            return OrderErrors.MixedCurrencies;
        }

        var address = ShippingAddress.Create(shippingAddress);
        if (address.IsFailure)
        {
            return address.Error;
        }

        var order = new Order(id, customerId, paymentMethodId, currency, 0m, address.Value, expiresAt, now);
        decimal total = 0m;
        foreach (var draft in itemDrafts)
        {
            var item = OrderItem.Create(id, draft);
            if (item.IsFailure)
            {
                return item.Error;
            }

            if (total > decimal.MaxValue - item.Value.LineTotal)
            {
                return OrderErrors.InvalidItem;
            }

            total += item.Value.LineTotal;
            order._items.Add(item.Value);
        }

        order.Total = total;
        return Result.Success(order);
    }

    public Result MarkInventoryReserved(Guid reservationId, DateTimeOffset reservationExpiresAt, DateTimeOffset now)
    {
        if (reservationId == Guid.Empty || reservationExpiresAt <= now)
        {
            return OrderErrors.WorkflowIdentityConflict;
        }

        if (InventoryReservationId is not null)
        {
            return InventoryReservationId == reservationId
                ? Result.Success()
                : OrderErrors.WorkflowIdentityConflict;
        }

        if (Status != OrderStatus.AwaitingInventory)
        {
            return OrderErrors.InvalidState;
        }

        InventoryReservationId = reservationId;
        ExpiresAt = reservationExpiresAt < ExpiresAt ? reservationExpiresAt : ExpiresAt;
        Status = OrderStatus.PaymentAuthorizing;
        Touch(now);
        return Result.Success();
    }

    public Result RequirePaymentAction(Guid paymentAttemptId, DateTimeOffset paymentExpiresAt, DateTimeOffset now)
    {
        if (paymentAttemptId == Guid.Empty)
        {
            return OrderErrors.WorkflowIdentityConflict;
        }

        if (PaymentAttemptId is not null && PaymentAttemptId != paymentAttemptId)
        {
            return OrderErrors.WorkflowIdentityConflict;
        }

        if (Status is OrderStatus.PaymentAuthorized or OrderStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status == OrderStatus.AwaitingPaymentAction && PaymentAttemptId == paymentAttemptId)
        {
            return Result.Success();
        }

        if (Status != OrderStatus.PaymentAuthorizing)
        {
            return OrderErrors.InvalidState;
        }

        PaymentAttemptId = paymentAttemptId;
        if (paymentExpiresAt < ExpiresAt)
        {
            ExpiresAt = paymentExpiresAt;
        }

        Status = OrderStatus.AwaitingPaymentAction;
        Touch(now);
        return Result.Success();
    }

    public Result MarkPaymentAuthorized(
        Guid paymentAttemptId,
        decimal amount,
        string currencyCode,
        DateTimeOffset now)
    {
        if (amount != Total || !string.Equals(currencyCode, CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return OrderErrors.PaymentAmountMismatch;
        }

        if (PaymentAttemptId is not null && PaymentAttemptId != paymentAttemptId)
        {
            return OrderErrors.WorkflowIdentityConflict;
        }

        if (Status is OrderStatus.PaymentAuthorized or OrderStatus.Confirmed)
        {
            return Result.Success();
        }

        if (Status is not (OrderStatus.PaymentAuthorizing or OrderStatus.AwaitingPaymentAction))
        {
            return OrderErrors.InvalidState;
        }

        PaymentAttemptId = paymentAttemptId;
        Status = OrderStatus.PaymentAuthorized;
        Touch(now);
        return Result.Success();
    }

    public Result Confirm(Guid reservationId, DateTimeOffset now)
    {
        if (Status == OrderStatus.Confirmed)
        {
            return InventoryReservationId == reservationId
                ? Result.Success()
                : OrderErrors.WorkflowIdentityConflict;
        }

        if (Status != OrderStatus.PaymentAuthorized || InventoryReservationId != reservationId)
        {
            return OrderErrors.InvalidState;
        }

        Status = OrderStatus.Confirmed;
        ConfirmedAt = now;
        Touch(now);
        return Result.Success();
    }

    public Result Cancel(string reasonCode, DateTimeOffset now)
    {
        if (Status == OrderStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status is OrderStatus.Confirmed or OrderStatus.Expired ||
            string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 64)
        {
            return OrderErrors.InvalidState;
        }

        Status = OrderStatus.Cancelled;
        TerminalReasonCode = reasonCode;
        CancelledAt = now;
        Touch(now);
        return Result.Success();
    }

    public Result Expire(DateTimeOffset now)
    {
        if (Status == OrderStatus.Expired)
        {
            return Result.Success();
        }

        if (IsTerminal)
        {
            return OrderErrors.InvalidState;
        }

        if (now < ExpiresAt)
        {
            return OrderErrors.NotExpired;
        }

        Status = OrderStatus.Expired;
        TerminalReasonCode = "checkout_expired";
        ExpiredAt = now;
        Touch(now);
        return Result.Success();
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
    }
}
