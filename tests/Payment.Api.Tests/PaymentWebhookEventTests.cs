using System.Globalization;
using Payment.Api.Persistence;

namespace Payment.Api.Tests;

public sealed class PaymentWebhookEventTests
{
    [Fact]
    public void SetupReceiptStartsUnprocessedAndKeepsProviderIdentity()
    {
        var id = Guid.NewGuid();
        var receivedAt = DateTimeOffset.Parse(
            "2026-08-17T00:00:00Z",
            CultureInfo.InvariantCulture);

        var webhookEvent = PaymentWebhookEvent.CreateSetup(
            id,
            "evt_123",
            "setup_intent.succeeded",
            "seti_123",
            receivedAt);

        Assert.Equal(id, webhookEvent.Id);
        Assert.Equal("evt_123", webhookEvent.ProviderEventId);
        Assert.Equal("setup_intent.succeeded", webhookEvent.EventType);
        Assert.Equal("seti_123", webhookEvent.ProviderSetupIntentId);
        Assert.Null(webhookEvent.ProviderPaymentIntentId);
        Assert.Equal(receivedAt, webhookEvent.ReceivedAt);
        Assert.Null(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void OrderPaymentReceiptKeepsPaymentIntentIdentity()
    {
        var receivedAt = DateTimeOffset.Parse(
            "2026-08-17T00:00:00Z",
            CultureInfo.InvariantCulture);

        var webhookEvent = PaymentWebhookEvent.CreateOrderPayment(
            Guid.NewGuid(),
            "evt_payment_123",
            "payment_intent.requires_action",
            "pi_123",
            receivedAt);

        Assert.Equal("pi_123", webhookEvent.ProviderPaymentIntentId);
        Assert.Null(webhookEvent.ProviderSetupIntentId);
        Assert.Equal(receivedAt, webhookEvent.ReceivedAt);
        Assert.Null(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void MarkProcessedIsIdempotentAndPreservesFirstCompletionTime()
    {
        var webhookEvent = PaymentWebhookEvent.CreateSetup(
            Guid.NewGuid(),
            "evt_123",
            "setup_intent.succeeded",
            "seti_123",
            DateTimeOffset.Parse(
                "2026-08-17T00:00:00Z",
                CultureInfo.InvariantCulture));
        var first = DateTimeOffset.Parse(
            "2026-08-17T00:00:01Z",
            CultureInfo.InvariantCulture);
        var second = first.AddMinutes(1);

        webhookEvent.MarkProcessed(first);
        webhookEvent.MarkProcessed(second);

        Assert.Equal(first, webhookEvent.ProcessedAt);
    }
}
