namespace Payment.Api.Features.OrderPayments.GettingAction.V1;

internal sealed record OrderPaymentActionResponse(Guid PaymentAttemptId, string Type, string ClientSecret);
