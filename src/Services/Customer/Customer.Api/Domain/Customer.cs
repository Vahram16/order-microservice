using System.Net.Mail;

namespace Customer.Api.Domain;

public enum CustomerStatus
{
    Active = 1,
    Suspended = 2,
    Deactivated = 3
}

public sealed class Customer
{
    public const int MaximumSavedAddresses = 20;

    private readonly List<CustomerAddress> _addresses = [];

    private Customer()
    {
    }

    private Customer(
        Guid id,
        string identityProvider,
        string identitySubject,
        string? firstName,
        string? lastName,
        string? email,
        DateTimeOffset now)
    {
        Id = id;
        IdentityProvider = Required(identityProvider, nameof(identityProvider), 32).ToLowerInvariant();
        IdentitySubject = Required(identitySubject, nameof(identitySubject), 255);
        FirstName = Optional(firstName, nameof(firstName), 100);
        LastName = Optional(lastName, nameof(lastName), 100);
        Email = NormalizeEmail(email);
        Status = CustomerStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public string IdentityProvider { get; private set; } = null!;
    public string IdentitySubject { get; private set; } = null!;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public CustomerStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<CustomerAddress> Addresses => _addresses.AsReadOnly();

    public static Customer Register(
        string identityProvider,
        string identitySubject,
        string? firstName,
        string? lastName,
        string? email,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            identityProvider,
            identitySubject,
            firstName,
            lastName,
            email,
            now);

    public void EnsureExpectedVersion(long expectedVersion)
    {
        if (expectedVersion <= 0 || Version != expectedVersion)
        {
            throw new CustomerVersionMismatchException(expectedVersion, Version);
        }
    }

    public CustomerAddress? FindAddress(Guid addressId) =>
        _addresses.SingleOrDefault(address => address.Id == addressId);

    public void UpdateDetails(
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        DateTimeOffset now)
    {
        EnsureActive();

        var normalizedFirstName = Optional(firstName, nameof(firstName), 100);
        var normalizedLastName = Optional(lastName, nameof(lastName), 100);
        var normalizedEmail = NormalizeEmail(email);
        var normalizedPhoneNumber = Optional(phoneNumber, nameof(phoneNumber), 32);

        FirstName = normalizedFirstName;
        LastName = normalizedLastName;
        Email = normalizedEmail;
        PhoneNumber = normalizedPhoneNumber;
        Touch(now);
    }

    public CustomerAddress AddAddress(Guid addressId, AddressData data, DateTimeOffset now)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);

        var existing = FindAddress(addressId);
        if (existing is not null)
        {
            if (existing.Matches(data))
            {
                return existing;
            }

            throw new CustomerIdempotencyConflictException(addressId);
        }

        if (_addresses.Count >= MaximumSavedAddresses)
        {
            throw new CustomerDomainException(
                "customer.address_limit_exceeded",
                $"A customer can save at most {MaximumSavedAddresses} addresses.");
        }

        var address = CustomerAddress.Create(addressId, Id, data, now);

        if (address.IsDefaultShipping)
        {
            ClearDefaultShipping(now);
        }

        if (address.IsDefaultBilling)
        {
            ClearDefaultBilling(now);
        }

        _addresses.Add(address);
        Touch(now);
        return address;
    }

    public CustomerAddress UpdateAddress(Guid addressId, AddressData data, DateTimeOffset now)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);

        var address = GetAddress(addressId);
        address.Update(data, now);

        if (address.IsDefaultShipping)
        {
            ClearDefaultShipping(now, addressId);
        }

        if (address.IsDefaultBilling)
        {
            ClearDefaultBilling(now, addressId);
        }

        Touch(now);
        return address;
    }

    public void RemoveAddress(Guid addressId, DateTimeOffset now)
    {
        EnsureActive();
        var address = GetAddress(addressId);
        _addresses.Remove(address);
        Touch(now);
    }

    public bool CloseAccount(DateTimeOffset now)
    {
        if (Status == CustomerStatus.Deactivated)
        {
            return false;
        }

        FirstName = null;
        LastName = null;
        Email = null;
        PhoneNumber = null;
        _addresses.Clear();
        Status = CustomerStatus.Deactivated;
        Touch(now);
        return true;
    }

    private CustomerAddress GetAddress(Guid addressId) =>
        FindAddress(addressId) ?? throw new CustomerAddressNotFoundException(addressId);

    private void ClearDefaultShipping(DateTimeOffset now, Guid? exceptAddressId = null)
    {
        foreach (var address in _addresses.Where(address =>
                     address.IsDefaultShipping && address.Id != exceptAddressId))
        {
            address.ClearDefaultShipping(now);
        }
    }

    private void ClearDefaultBilling(DateTimeOffset now, Guid? exceptAddressId = null)
    {
        foreach (var address in _addresses.Where(address =>
                     address.IsDefaultBilling && address.Id != exceptAddressId))
        {
            address.ClearDefaultBilling(now);
        }
    }

    private void EnsureActive()
    {
        if (Status != CustomerStatus.Active)
        {
            throw new CustomerInactiveException(Status);
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = LaterOf(UpdatedAt, now);
        Version++;
    }

    private static DateTimeOffset LaterOf(DateTimeOffset current, DateTimeOffset candidate) =>
        candidate > current ? candidate : current;

    private static string Required(string value, string field, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new CustomerDomainException(
                "customer.validation",
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(string? value, string field, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, field, maximumLength);

    private static string? NormalizeEmail(string? value)
    {
        var normalized = Optional(value, nameof(Email), 320)?.ToLowerInvariant();
        if (normalized is null)
        {
            return null;
        }

        if (!MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new CustomerDomainException(
                "customer.invalid_email",
                "Email must be a valid email address.");
        }

        return normalized;
    }
}
