using Microservices.Primitives;

namespace Payment.Api.Domain;

public sealed class OrderPaymentAttempt
{
    private OrderPaymentAttempt() { }

    private OrderPaymentAttempt(
        Guid id,
        Guid orderId,
        Guid paymentCustomerId,
        Guid paymentMethodId,
        decimal amount,
        string currencyCode,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        Id = id;
        OrderId = orderId;
        PaymentCustomerId = paymentCustomerId;
        PaymentMethodId = paymentMethodId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = OrderPaymentStatus.Pending;
        ExpiresAt = expiresAt;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid PaymentCustomerId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public string? ProviderPaymentIntentId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public OrderPaymentStatus Status { get; private set; }
    public string? RejectionCode { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Result<OrderPaymentAttempt> Create(
        Guid id,
        Guid orderId,
        Guid paymentCustomerId,
        Guid paymentMethodId,
        decimal amount,
        string currencyCode,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (id == Guid.Empty || orderId == Guid.Empty || paymentCustomerId == Guid.Empty || paymentMethodId == Guid.Empty)
        {
            return PaymentErrors.Validation("orderPayment", "Order payment identifiers cannot be empty.");
        }

        if (amount <= 0m || decimal.Round(amount, 2) != amount)
        {
            return PaymentErrors.Validation(nameof(amount), "Order payment amount must be positive with at most two decimal places.");
        }

        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return PaymentErrors.Validation(nameof(currencyCode), "Order payment currency is required.");
        }

        var currency = currencyCode.Trim().ToUpperInvariant();
        if (currency.Length != 3 || currency.Any(character => character is not (>= 'A' and <= 'Z')))
        {
            return PaymentErrors.Validation(nameof(currencyCode), "Order payment currency must be a three-letter ASCII code.");
        }

        if (expiresAt <= now)
        {
            return PaymentErrors.Validation(nameof(expiresAt), "Order payment deadline must be in the future.");
        }

        return Result.Success(new OrderPaymentAttempt(
            id,
            orderId,
            paymentCustomerId,
            paymentMethodId,
            amount,
            currency,
            expiresAt,
            now));
    }

    public bool MatchesRequest(
        Guid customerId,
        Guid paymentMethodId,
        decimal amount,
        string currencyCode,
        DateTimeOffset expiresAt,
        Guid actualCustomerId) =>
        actualCustomerId == customerId &&
        PaymentMethodId == paymentMethodId &&
        Amount == amount &&
        string.Equals(CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase) &&
        ExpiresAt == expiresAt;

    public Result AssignProviderPaymentIntent(string providerPaymentIntentId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentIntentId) ||
            !string.Equals(providerPaymentIntentId, providerPaymentIntentId.Trim(), StringComparison.Ordinal) ||
            providerPaymentIntentId.Length > 255)
        {
            return PaymentErrors.Validation(nameof(providerPaymentIntentId), "Provider payment intent identifier is invalid.");
        }

        if (ProviderPaymentIntentId is not null)
        {
            return string.Equals(ProviderPaymentIntentId, providerPaymentIntentId, StringComparison.Ordinal)
                ? Result.Success()
                : PaymentErrors.OrderPaymentConflict;
        }

        ProviderPaymentIntentId = providerPaymentIntentId;
        Touch(now);
        return Result.Success();
    }

    public Result RequireCustomerAction(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.RequiresCustomerAction)
        {
            return Result.Success();
        }

        if (Status != OrderPaymentStatus.Pending)
        {
            return PaymentErrors.OrderPaymentInvalidState;
        }

        Status = OrderPaymentStatus.RequiresCustomerAction;
        Touch(now);
        return Result.Success();
    }

    public Result Authorize(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.Authorized)
        {
            return Result.Success();
        }

        if (Status is not (OrderPaymentStatus.Pending or OrderPaymentStatus.RequiresCustomerAction))
        {
            return PaymentErrors.OrderPaymentInvalidState;
        }

        Status = OrderPaymentStatus.Authorized;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result Reject(string rejectionCode, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(rejectionCode) || rejectionCode.Length > 128)
        {
            return PaymentErrors.Validation(nameof(rejectionCode), "Payment rejection code is invalid.");
        }

        if (Status == OrderPaymentStatus.Rejected)
        {
            return string.Equals(RejectionCode, rejectionCode, StringComparison.Ordinal)
                ? Result.Success()
                : PaymentErrors.OrderPaymentConflict;
        }

        if (Status is not (OrderPaymentStatus.Pending or OrderPaymentStatus.RequiresCustomerAction))
        {
            return PaymentErrors.OrderPaymentInvalidState;
        }

        Status = OrderPaymentStatus.Rejected;
        RejectionCode = rejectionCode;
        Touch(now);
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.Cancelled)
        {
            return Result.Success();
        }

        if (Status == OrderPaymentStatus.Rejected)
        {
            return Result.Success();
        }

        Status = OrderPaymentStatus.Cancelled;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
    }
}
