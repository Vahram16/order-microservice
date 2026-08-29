using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Order.Api.Domain;
using Order.Api.Integration;
using OrderAggregate = Order.Api.Domain.Order;

namespace Order.Api.Tests;

public sealed class OrderWorkflowReliabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    [Fact]
    public void OptimisticConcurrencyFailureIsTransient()
    {
        var rule = new OrderPersistenceExceptionRule();

        var disposition = rule.Classify(new DbUpdateConcurrencyException());

        Assert.Equal(ConsumerExceptionDisposition.Transient, disposition);
    }

    [Fact]
    public void StalePaymentAuthorizedOrderRedrivesInventoryCommit()
    {
        var order = CreatePaymentAuthorizedOrder();

        var action = OrderWorkflowRecoveryPolicy.GetAction(
            order,
            Now.AddMinutes(10),
            StaleAfter);

        Assert.Equal(OrderWorkflowRecoveryAction.CommitInventory, action);
    }

    [Fact]
    public void StalePaymentCapturingOrderRedrivesProviderReconciliation()
    {
        var order = CreatePaymentAuthorizedOrder();
        Assert.True(order.MarkInventoryCommitted(
            order.InventoryReservationId!.Value,
            Now.AddSeconds(3)).IsSuccess);

        var action = OrderWorkflowRecoveryPolicy.GetAction(
            order,
            Now.AddMinutes(10),
            StaleAfter);

        Assert.Equal(OrderWorkflowRecoveryAction.CapturePayment, action);
    }

    [Fact]
    public void FreshWorkflowStateIsNotRedriven()
    {
        var order = CreatePaymentAuthorizedOrder();

        var action = OrderWorkflowRecoveryPolicy.GetAction(
            order,
            Now.AddMinutes(4),
            StaleAfter);

        Assert.Equal(OrderWorkflowRecoveryAction.None, action);
    }

    private static OrderAggregate CreatePaymentAuthorizedOrder()
    {
        var placement = OrderAggregate.Place(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [new OrderItemDraft(Guid.NewGuid(), "SKU-1", "Historical product", 1, 12.50m, "USD")],
            new ShippingAddressData("Recipient", "Line 1", null, "Yerevan", null, "0010", "AM", null),
            Now.AddMinutes(15),
            Now);
        Assert.True(placement.IsSuccess);

        var order = placement.Value;
        Assert.True(order.MarkInventoryReserved(
            Guid.NewGuid(),
            Now.AddMinutes(10),
            Now.AddSeconds(1)).IsSuccess);
        Assert.True(order.MarkPaymentAuthorized(
            Guid.NewGuid(),
            order.Total,
            order.CurrencyCode,
            Now.AddSeconds(2)).IsSuccess);
        return order;
    }
}
