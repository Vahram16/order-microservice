using Payment.Api.Persistence;

namespace Payment.Api.Tests;

public sealed class PaymentWebhookEventTests
{
    [Fact]
    public void DurableReceiptStartsUnprocessedAndKeepsProviderIdentity()
    {
        var id = Guid.NewGuid();
        var receivedAt = DateTimeOffset.Parse("2026-08-17T00:00:00Z");

        var webhookEvent = PaymentWebhookEvent.Create(
            id,
            "evt_123",
            "setup_intent.succeeded",
            "seti_123",
            receivedAt);

        Assert.Equal(id, webhookEvent.Id);
        Assert.Equal("evt_123", webhookEvent.ProviderEventId);
        Assert.Equal("setup_intent.succeeded", webhookEvent.EventType);
        Assert.Equal("seti_123", webhookEvent.ProviderSetupIntentId);
        Assert.Equal(receivedAt, webhookEvent.ReceivedAt);
        Assert.Null(webhookEvent.ProcessedAt);
    }

    [Fact]
    public void MarkProcessedIsIdempotentAndPreservesFirstCompletionTime()
    {
        var webhookEvent = PaymentWebhookEvent.Create(
            Guid.NewGuid(),
            "evt_123",
            "setup_intent.succeeded",
            "seti_123",
            DateTimeOffset.Parse("2026-08-17T00:00:00Z"));
        var first = DateTimeOffset.Parse("2026-08-17T00:00:01Z");
        var second = first.AddMinutes(1);

        webhookEvent.MarkProcessed(first);
        webhookEvent.MarkProcessed(second);

        Assert.Equal(first, webhookEvent.ProcessedAt);
    }
}
