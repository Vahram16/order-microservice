using Microservices.Primitives;

namespace Payment.Api.Domain;

public sealed class OrderPaymentAttempt
{
    private OrderPaymentAttempt() { }

    private OrderPaymentAttempt(Guid id, Guid orderId, Guid paymentCustomerId, Guid paymentMethodId, decimal amount, string currencyCode, DateTimeOffset expiresAt, DateTimeOffset now)
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
    public string? ProviderRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public OrderPaymentStatus Status { get; private set; }
    public string? RejectionCode { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Result<OrderPaymentAttempt> Create(Guid id, Guid orderId, Guid paymentCustomerId, Guid paymentMethodId, decimal amount, string currencyCode, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (id == Guid.Empty || orderId == Guid.Empty || paymentCustomerId == Guid.Empty || paymentMethodId == Guid.Empty)
            return PaymentErrors.Validation("orderPayment", "Order payment identifiers cannot be empty.");
        if (!CurrencyAmount.TryNormalizeCurrencyCode(currencyCode, out var currency) || amount <= 0m || !CurrencyAmount.HasValidScale(amount, currency))
            return PaymentErrors.Validation(nameof(amount), "Order payment amount or currency precision is invalid.");
        if (expiresAt <= now) return PaymentErrors.Validation(nameof(expiresAt), "Order payment deadline must be in the future.");
        return Result.Success(new OrderPaymentAttempt(id, orderId, paymentCustomerId, paymentMethodId, amount, currency, expiresAt, now));
    }

    public bool MatchesRequest(Guid customerId, Guid paymentMethodId, decimal amount, string currencyCode, DateTimeOffset expiresAt, Guid actualCustomerId) =>
        actualCustomerId == customerId && PaymentMethodId == paymentMethodId && Amount == amount && string.Equals(CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase) && ExpiresAt == expiresAt;

    public Result AssignProviderPaymentIntent(string providerPaymentIntentId, DateTimeOffset now)
    {
        if (!IsValidProviderId(providerPaymentIntentId)) return PaymentErrors.Validation(nameof(providerPaymentIntentId), "Provider payment intent identifier is invalid.");
        if (ProviderPaymentIntentId is not null)
            return string.Equals(ProviderPaymentIntentId, providerPaymentIntentId, StringComparison.Ordinal) ? Result.Success() : PaymentErrors.OrderPaymentConflict;
        ProviderPaymentIntentId = providerPaymentIntentId;
        Touch(now);
        return Result.Success();
    }

    public Result RequireCustomerAction(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.RequiresCustomerAction) return Result.Success();
        if (Status != OrderPaymentStatus.Pending) return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.RequiresCustomerAction;
        Touch(now);
        return Result.Success();
    }

    public Result Authorize(DateTimeOffset now)
    {
        if (Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured) return Result.Success();
        if (Status is not (OrderPaymentStatus.Pending or OrderPaymentStatus.RequiresCustomerAction)) return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.Authorized;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result Capture(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.Captured) return Result.Success();
        if (Status != OrderPaymentStatus.Authorized) return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.Captured;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result FailCapture(string rejectionCode, DateTimeOffset now)
    {
        var validation = ValidateRejectionCode(rejectionCode);
        if (validation.IsFailure) return validation.Error;
        if (Status == OrderPaymentStatus.CaptureFailed)
            return string.Equals(RejectionCode, rejectionCode, StringComparison.Ordinal) ? Result.Success() : PaymentErrors.OrderPaymentConflict;
        if (Status != OrderPaymentStatus.Authorized) return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.CaptureFailed;
        RejectionCode = rejectionCode;
        Touch(now);
        return Result.Success();
    }

    public Result Reject(string rejectionCode, DateTimeOffset now)
    {
        var validation = ValidateRejectionCode(rejectionCode);
        if (validation.IsFailure) return validation.Error;
        if (Status == OrderPaymentStatus.Rejected)
            return string.Equals(RejectionCode, rejectionCode, StringComparison.Ordinal) ? Result.Success() : PaymentErrors.OrderPaymentConflict;
        if (Status is not (OrderPaymentStatus.Pending or OrderPaymentStatus.RequiresCustomerAction)) return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.Rejected;
        RejectionCode = rejectionCode;
        Touch(now);
        return Result.Success();
    }

    public Result RequestCancellation(DateTimeOffset now)
    {
        if (Status is OrderPaymentStatus.Cancelled or OrderPaymentStatus.Rejected or OrderPaymentStatus.Refunded or OrderPaymentStatus.CancellationRequested or OrderPaymentStatus.RefundPending or OrderPaymentStatus.RefundFailed)
            return Result.Success();
        if (Status is not (OrderPaymentStatus.Pending or OrderPaymentStatus.RequiresCustomerAction or OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured or OrderPaymentStatus.CaptureFailed))
            return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.CancellationRequested;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result RecordCompensationFailure(string failureCode, DateTimeOffset now)
    {
        var validation = ValidateRejectionCode(failureCode);
        if (validation.IsFailure) return validation.Error;
        if (Status is not (OrderPaymentStatus.CancellationRequested or OrderPaymentStatus.RefundPending or OrderPaymentStatus.RefundFailed))
            return PaymentErrors.OrderPaymentInvalidState;
        RejectionCode = failureCode;
        Touch(now);
        return Result.Success();
    }

    public Result ObserveCapturedDuringCancellation(DateTimeOffset now)
    {
        if (Status == OrderPaymentStatus.Captured) return Result.Success();
        if (Status is not (OrderPaymentStatus.CancellationRequested or OrderPaymentStatus.Cancelled or OrderPaymentStatus.Rejected or OrderPaymentStatus.CaptureFailed))
            return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.Captured;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result MarkRefundPending(string providerRefundId, DateTimeOffset now)
    {
        var validation = ValidateRefundTransition(providerRefundId, allowRefunded: true, OrderPaymentStatus.Captured, OrderPaymentStatus.CancellationRequested, OrderPaymentStatus.RefundPending, OrderPaymentStatus.RefundFailed);
        if (validation.IsFailure) return validation.Error;
        if (Status == OrderPaymentStatus.Refunded) return Result.Success();
        ProviderRefundId ??= providerRefundId;
        Status = OrderPaymentStatus.RefundPending;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result MarkRefunded(string providerRefundId, DateTimeOffset now)
    {
        var validation = ValidateRefundTransition(providerRefundId, allowRefunded: true, OrderPaymentStatus.Captured, OrderPaymentStatus.CancellationRequested, OrderPaymentStatus.RefundPending, OrderPaymentStatus.RefundFailed);
        if (validation.IsFailure) return validation.Error;
        if (Status == OrderPaymentStatus.Refunded) return Result.Success();
        ProviderRefundId ??= providerRefundId;
        Status = OrderPaymentStatus.Refunded;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    public Result FailRefund(string providerRefundId, string rejectionCode, DateTimeOffset now)
    {
        var rejectionValidation = ValidateRejectionCode(rejectionCode);
        if (rejectionValidation.IsFailure) return rejectionValidation.Error;
        var transitionValidation = ValidateRefundTransition(providerRefundId, allowRefunded: false, OrderPaymentStatus.Captured, OrderPaymentStatus.CancellationRequested, OrderPaymentStatus.RefundPending, OrderPaymentStatus.RefundFailed);
        if (transitionValidation.IsFailure) return transitionValidation.Error;
        if (Status == OrderPaymentStatus.RefundFailed)
            return string.Equals(RejectionCode, rejectionCode, StringComparison.Ordinal) ? Result.Success() : PaymentErrors.OrderPaymentConflict;
        ProviderRefundId ??= providerRefundId;
        Status = OrderPaymentStatus.RefundFailed;
        RejectionCode = rejectionCode;
        Touch(now);
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status is OrderPaymentStatus.Cancelled or OrderPaymentStatus.Rejected) return Result.Success();
        if (Status is OrderPaymentStatus.Captured or OrderPaymentStatus.RefundPending or OrderPaymentStatus.Refunded or OrderPaymentStatus.RefundFailed)
            return PaymentErrors.OrderPaymentInvalidState;
        Status = OrderPaymentStatus.Cancelled;
        RejectionCode = null;
        Touch(now);
        return Result.Success();
    }

    private Result ValidateRefundTransition(string providerRefundId, bool allowRefunded, params OrderPaymentStatus[] allowedStatuses)
    {
        if (!IsValidProviderId(providerRefundId)) return PaymentErrors.Validation(nameof(providerRefundId), "Provider refund identifier is invalid.");
        if (ProviderRefundId is not null && !string.Equals(ProviderRefundId, providerRefundId, StringComparison.Ordinal)) return PaymentErrors.OrderPaymentConflict;
        if (allowRefunded && Status == OrderPaymentStatus.Refunded) return Result.Success();
        return allowedStatuses.Contains(Status) ? Result.Success() : PaymentErrors.OrderPaymentInvalidState;
    }

    private static bool IsValidProviderId(string value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal) && value.Length <= 255;

    private static Result ValidateRejectionCode(string rejectionCode) =>
        string.IsNullOrWhiteSpace(rejectionCode) || rejectionCode.Length > 128
            ? PaymentErrors.Validation(nameof(rejectionCode), "Payment rejection code is invalid.")
            : Result.Success();

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now > UpdatedAt ? now : UpdatedAt;
        Version++;
    }
}
