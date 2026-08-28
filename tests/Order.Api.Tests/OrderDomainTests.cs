using Order.Api.Domain;
using OrderAggregate = Order.Api.Domain.Order;

namespace Order.Api.Tests;

public sealed class OrderDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HappyPathRequiresCapturedPaymentBeforeConfirmation()
    {
        var order = CreateOrder(); var reservationId = Guid.NewGuid(); var paymentAttemptId = Guid.NewGuid();
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(1)).IsSuccess);
        Assert.True(order.MarkPaymentAuthorized(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(2)).IsSuccess);
        Assert.True(order.MarkInventoryCommitted(reservationId, Now.AddSeconds(3)).IsSuccess);
        Assert.Equal(OrderStatus.PaymentCapturing, order.Status); Assert.Null(order.ConfirmedAt);
        Assert.True(order.ConfirmPaymentCaptured(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(4)).IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void PaymentActionIsDurableNormalState()
    {
        var order = CreateOrder(); var reservationId = Guid.NewGuid(); var paymentAttemptId = Guid.NewGuid();
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(1)).IsSuccess);
        Assert.True(order.RequirePaymentAction(paymentAttemptId, Now.AddMinutes(5), Now.AddSeconds(2)).IsSuccess);
        Assert.Equal(OrderStatus.AwaitingPaymentAction, order.Status);
        Assert.True(order.MarkPaymentAuthorized(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(3)).IsSuccess);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
    }

    [Fact]
    public void CaptureFailureCancelsWithoutMutatingHistoricalItems()
    {
        var order = CreateOrder(); var originalItem = order.Items.Single(); var reservationId = Guid.NewGuid(); var paymentAttemptId = Guid.NewGuid();
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(1)).IsSuccess);
        Assert.True(order.MarkPaymentAuthorized(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(2)).IsSuccess);
        Assert.True(order.MarkInventoryCommitted(reservationId, Now.AddSeconds(3)).IsSuccess);
        Assert.True(order.FailPaymentCapture(paymentAttemptId, "capture_failed", Now.AddSeconds(4)).IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status); Assert.Equal("SKU-1", originalItem.Sku); Assert.Equal(12.50m, originalItem.UnitPrice);
    }

    [Fact]
    public void FailedPaymentAmountTransitionIsFailureAtomic()
    {
        var order = CreateOrder(); var reservationId = Guid.NewGuid(); var paymentAttemptId = Guid.NewGuid();
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(1)).IsSuccess); var version = order.Version;
        var result = order.MarkPaymentAuthorized(paymentAttemptId, order.Total + 1m, order.CurrencyCode, Now.AddSeconds(2));
        Assert.True(result.IsFailure); Assert.Equal(OrderStatus.PaymentAuthorizing, order.Status); Assert.Null(order.PaymentAttemptId); Assert.Equal(version, order.Version);
    }

    [Fact]
    public void DuplicateWorkflowFactsAreIdempotent()
    {
        var order = CreateOrder(); var reservationId = Guid.NewGuid(); var paymentAttemptId = Guid.NewGuid();
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(1)).IsSuccess);
        Assert.True(order.MarkInventoryReserved(reservationId, Now.AddMinutes(10), Now.AddSeconds(2)).IsSuccess);
        Assert.True(order.MarkPaymentAuthorized(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(3)).IsSuccess);
        Assert.True(order.MarkPaymentAuthorized(paymentAttemptId, order.Total, order.CurrencyCode, Now.AddSeconds(4)).IsSuccess);
    }

    private static OrderAggregate CreateOrder()
    {
        var result = OrderAggregate.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [new OrderItemDraft(Guid.NewGuid(), "SKU-1", "Historical product", 2, 12.50m, "USD")], new ShippingAddressData("Recipient", "Line 1", null, "Yerevan", null, "0010", "AM", null), Now.AddMinutes(15), Now);
        Assert.True(result.IsSuccess); return result.Value;
    }
}
