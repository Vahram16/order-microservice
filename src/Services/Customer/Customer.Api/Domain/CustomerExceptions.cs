namespace Customer.Api.Domain;

public class CustomerDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class CustomerNotFoundException()
    : CustomerDomainException("customer.not_found", "Customer was not found.");

public sealed class CustomerAddressNotFoundException(Guid addressId)
    : CustomerDomainException(
        "customer.address_not_found",
        $"Customer address '{addressId}' was not found.");

public sealed class CustomerInactiveException(CustomerStatus status)
    : CustomerDomainException(
        "customer.inactive",
        $"Customer data cannot be changed while the customer status is '{status}'.");

public sealed class CustomerVersionMismatchException(long expectedVersion, long currentVersion)
    : CustomerDomainException(
        "customer.version_mismatch",
        $"Customer version '{expectedVersion}' does not match current version '{currentVersion}'.")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long CurrentVersion { get; } = currentVersion;
}

public sealed class CustomerIdempotencyConflictException(Guid addressId)
    : CustomerDomainException(
        "customer.idempotency_conflict",
        $"Idempotency key '{addressId}' was already used with different address data.");
