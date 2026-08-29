namespace Payment.Api.Persistence;

internal static class PaymentDatabaseConstraints
{
    public const string CustomerId = "ux_payment_customers_customer_id";
    public const string CustomerIdentity = "ux_payment_customers_identity";
    public const string ProviderCustomer = "ux_payment_customers_provider_customer";
    public const string ProviderPaymentMethod = "ux_payment_methods_provider_id";
    public const string DefaultPaymentMethod = "ux_payment_methods_default";
    public const string PaymentMethodSetupPrimaryKey = "pk_payment_method_setups";
    public const string ProviderSetupIntent = "ux_payment_method_setups_provider_intent";
    public const string ProviderWebhookEvent = "ux_payment_webhook_events_provider_event";
    public const string OrderPaymentOrder = "ux_order_payment_attempts_order_id";
    public const string ProviderPaymentIntent = "ux_order_payment_attempts_provider_intent";
    public const string ProviderRefund = "ux_order_payment_attempts_provider_refund";
}
