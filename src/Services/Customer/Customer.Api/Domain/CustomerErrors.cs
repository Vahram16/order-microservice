using Microservices.Primitives;

namespace Customer.Api.Domain;

public static class CustomerErrors
{
    public static OperationError AddressNotFound { get; } = OperationError.MissingResource(
        "customer.address_not_found",
        "The requested customer address was not found.");

    public static OperationError Inactive { get; } = OperationError.StateConflict(
        "customer.inactive",
        "Customer data cannot be changed in its current state.");

    public static OperationError AddressLimitExceeded(int maximum) => OperationError.StateConflict(
        "customer.address_limit_exceeded",
        "The maximum number of saved customer addresses has been reached.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["maximum"] = maximum
        });

    public static OperationError AddressIdentityConflict { get; } = OperationError.StateConflict(
        "customer.address_identity_conflict",
        "An address with the same identity already exists with different data.");

    public static OperationError VersionMismatch { get; } = OperationError.ConcurrencyConflict(
        "customer.version_mismatch",
        "The customer changed after the supplied version was issued. Reload the customer and retry.");

    public static OperationError Validation(string field, string description) => OperationError.InvalidInput(
        "customer.validation",
        description,
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = field
        });

    public static OperationError InvalidEmail { get; } = OperationError.InvalidInput(
        "customer.invalid_email",
        "Email must be a valid email address.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = "email"
        });

    public static OperationError InvalidCountryCode { get; } = OperationError.InvalidInput(
        "customer.invalid_country_code",
        "Country code must be an ISO 3166-1 alpha-2 code.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = "countryCode"
        });

    public static OperationError InvalidAddressId { get; } = OperationError.InvalidInput(
        "customer.invalid_address_id",
        "Address identifier cannot be empty.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = "addressId"
        });
}
