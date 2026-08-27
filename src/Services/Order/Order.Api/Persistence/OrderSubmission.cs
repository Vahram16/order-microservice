namespace Order.Api.Persistence;

internal sealed class OrderSubmission
{
    private OrderSubmission() { }

    private OrderSubmission(Guid customerId, Guid idempotencyKey, string requestFingerprint, Guid orderId, DateTimeOffset createdAt)
    {
        CustomerId = customerId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        OrderId = orderId;
        CreatedAt = createdAt;
    }

    public Guid CustomerId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = null!;
    public Guid OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderSubmission Create(Guid customerId, Guid idempotencyKey, string requestFingerprint, Guid orderId, DateTimeOffset now) =>
        new(customerId, idempotencyKey, requestFingerprint, orderId, now);
}
