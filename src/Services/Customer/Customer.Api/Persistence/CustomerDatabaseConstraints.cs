namespace Customer.Api.Persistence;

internal static class CustomerDatabaseConstraints
{
    internal const string Identity = "IX_customers_IdentityProvider_IdentitySubject";
    internal const string AddressPrimaryKey = "PK_customer_addresses";
    internal const string DefaultShipping = "UX_customer_addresses_default_shipping";
    internal const string DefaultBilling = "UX_customer_addresses_default_billing";
}
