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
        IdentityProvider = Required(identityProvider, nameof(identityProvider), 32);
        IdentitySubject = Required(identitySubject, nameof(identitySubject), 255);
        FirstName = Optional(firstName, nameof(firstName), 100);
        LastName = Optional(lastName, nameof(lastName), 100);
        Email = Optional(email, nameof(email), 320)?.ToLowerInvariant();
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
    public IReadOnlyCollection<CustomerAddress> Addresses => _addresses;

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

    public void UpdateDetails(
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        DateTimeOffset now)
    {
        EnsureActive();
        FirstName = Optional(firstName, nameof(firstName), 100);
        LastName = Optional(lastName, nameof(lastName), 100);
        Email = Optional(email, nameof(email), 320)?.ToLowerInvariant();
        PhoneNumber = Optional(phoneNumber, nameof(phoneNumber), 32);
        Touch(now);
    }

    public CustomerAddress AddAddress(AddressData data, DateTimeOffset now)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);

        if (_addresses.Count >= MaximumSavedAddresses)
        {
            throw new CustomerDomainException(
                $"A customer can save at most {MaximumSavedAddresses} addresses.");
        }

        if (data.IsDefaultShipping)
        {
            ClearDefaultShipping(now);
        }

        if (data.IsDefaultBilling)
        {
            ClearDefaultBilling(now);
        }

        var address = CustomerAddress.Create(Id, data, now);
        _addresses.Add(address);
        Touch(now);
        return address;
    }

    public CustomerAddress UpdateAddress(Guid addressId, AddressData data, DateTimeOffset now)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(data);

        var address = GetAddress(addressId);
        if (data.IsDefaultShipping)
        {
            ClearDefaultShipping(now, addressId);
        }

        if (data.IsDefaultBilling)
        {
            ClearDefaultBilling(now, addressId);
        }

        address.Update(data, now);
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

    private CustomerAddress GetAddress(Guid addressId) =>
        _addresses.SingleOrDefault(address => address.Id == addressId)
        ?? throw new CustomerAddressNotFoundException(addressId);

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
            throw new CustomerDomainException(
                "Only an active customer can change customer details.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        if (now < UpdatedAt)
        {
            throw new CustomerDomainException("The update timestamp cannot move backwards.");
        }

        UpdatedAt = now;
        Version++;
    }

    private static string Required(string value, string field, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new CustomerDomainException(
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static string? Optional(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, field, maximumLength);
    }
}

public sealed class CustomerAddress
{
    private CustomerAddress()
    {
    }

    private CustomerAddress(Guid customerId, AddressData data, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        Apply(data);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string? Label { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public string Line1 { get; private set; } = null!;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = null!;
    public string? Region { get; private set; }
    public string PostalCode { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public string? PhoneNumber { get; private set; }
    public bool IsDefaultShipping { get; private set; }
    public bool IsDefaultBilling { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal static CustomerAddress Create(
        Guid customerId,
        AddressData data,
        DateTimeOffset now) => new(customerId, data, now);

    internal void Update(AddressData data, DateTimeOffset now)
    {
        Apply(data);
        UpdatedAt = now;
    }

    internal void ClearDefaultShipping(DateTimeOffset now)
    {
        IsDefaultShipping = false;
        UpdatedAt = now;
    }

    internal void ClearDefaultBilling(DateTimeOffset now)
    {
        IsDefaultBilling = false;
        UpdatedAt = now;
    }

    private void Apply(AddressData data)
    {
        Label = NormalizeOptional(data.Label, nameof(data.Label), 50);
        RecipientName = NormalizeRequired(data.RecipientName, nameof(data.RecipientName), 200);
        Line1 = NormalizeRequired(data.Line1, nameof(data.Line1), 200);
        Line2 = NormalizeOptional(data.Line2, nameof(data.Line2), 200);
        City = NormalizeRequired(data.City, nameof(data.City), 100);
        Region = NormalizeOptional(data.Region, nameof(data.Region), 100);
        PostalCode = NormalizeRequired(data.PostalCode, nameof(data.PostalCode), 32);
        CountryCode = NormalizeRequired(data.CountryCode, nameof(data.CountryCode), 2)
            .ToUpperInvariant();
        PhoneNumber = NormalizeOptional(data.PhoneNumber, nameof(data.PhoneNumber), 32);
        IsDefaultShipping = data.IsDefaultShipping;
        IsDefaultBilling = data.IsDefaultBilling;
    }

    private static string NormalizeRequired(string value, string field, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new CustomerDomainException(
                $"{field} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, string field, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, field, maximumLength);
}

public sealed record AddressData(
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber,
    bool IsDefaultShipping,
    bool IsDefaultBilling);

public class CustomerDomainException(string message) : Exception(message);

public sealed class CustomerNotFoundException() : CustomerDomainException("Customer was not found.");

public sealed class CustomerAddressNotFoundException(Guid addressId)
    : CustomerDomainException($"Customer address '{addressId}' was not found.");
