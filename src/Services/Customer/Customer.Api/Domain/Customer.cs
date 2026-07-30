using System.Net.Mail;
using Microservices.Primitives;

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
        IdentityProvider = identityProvider;
        IdentitySubject = identitySubject;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
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

    public static Result<Customer> Register(
        string identityProvider,
        string identitySubject,
        string? firstName,
        string? lastName,
        string? email,
        DateTimeOffset now)
    {
        var provider = Required(identityProvider, nameof(identityProvider), 32);
        if (provider.IsFailure)
        {
            return provider.Error;
        }

        var subject = RequiredOpaque(identitySubject, nameof(identitySubject), 255);
        if (subject.IsFailure)
        {
            return subject.Error;
        }

        var normalizedFirstName = Optional(firstName, nameof(firstName), 100);
        if (normalizedFirstName.IsFailure)
        {
            return normalizedFirstName.Error;
        }

        var normalizedLastName = Optional(lastName, nameof(lastName), 100);
        if (normalizedLastName.IsFailure)
        {
            return normalizedLastName.Error;
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail.IsFailure)
        {
            return normalizedEmail.Error;
        }

        return Result.Success(new Customer(
            Guid.NewGuid(),
            provider.Value.ToLowerInvariant(),
            subject.Value,
            normalizedFirstName.Value.Value,
            normalizedLastName.Value.Value,
            normalizedEmail.Value.Value,
            now));
    }

    public Result EnsureExpectedVersion(long expectedVersion) =>
        expectedVersion > 0 && Version == expectedVersion
            ? Result.Success()
            : CustomerErrors.VersionMismatch;

    public CustomerAddress? FindAddress(Guid addressId) =>
        _addresses.SingleOrDefault(address => address.Id == addressId);

    public Result UpdateDetails(
        string? firstName,
        string? lastName,
        string? email,
        string? phoneNumber,
        DateTimeOffset now)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error;
        }

        var normalizedFirstName = Optional(firstName, nameof(firstName), 100);
        if (normalizedFirstName.IsFailure)
        {
            return normalizedFirstName.Error;
        }

        var normalizedLastName = Optional(lastName, nameof(lastName), 100);
        if (normalizedLastName.IsFailure)
        {
            return normalizedLastName.Error;
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail.IsFailure)
        {
            return normalizedEmail.Error;
        }

        var normalizedPhoneNumber = Optional(phoneNumber, nameof(phoneNumber), 32);
        if (normalizedPhoneNumber.IsFailure)
        {
            return normalizedPhoneNumber.Error;
        }

        FirstName = normalizedFirstName.Value.Value;
        LastName = normalizedLastName.Value.Value;
        Email = normalizedEmail.Value.Value;
        PhoneNumber = normalizedPhoneNumber.Value.Value;
        Touch(now);
        return Result.Success();
    }

    public Result<CustomerAddress> AddAddress(Guid addressId, AddressData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error;
        }

        var existing = FindAddress(addressId);
        if (existing is not null)
        {
            var matches = existing.Matches(data);
            if (matches.IsFailure)
            {
                return matches.Error;
            }

            return matches.Value
                ? Result.Success(existing)
                : CustomerErrors.AddressIdentityConflict;
        }

        if (_addresses.Count >= MaximumSavedAddresses)
        {
            return CustomerErrors.AddressLimitExceeded(MaximumSavedAddresses);
        }

        var addressResult = CustomerAddress.Create(addressId, Id, data, now);
        if (addressResult.IsFailure)
        {
            return addressResult.Error;
        }

        var address = addressResult.Value;
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
        return Result.Success(address);
    }

    public Result<CustomerAddress> UpdateAddress(Guid addressId, AddressData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error;
        }

        var address = FindAddress(addressId);
        if (address is null)
        {
            return CustomerErrors.AddressNotFound;
        }

        var update = address.Update(data, now);
        if (update.IsFailure)
        {
            return update.Error;
        }

        if (address.IsDefaultShipping)
        {
            ClearDefaultShipping(now, addressId);
        }

        if (address.IsDefaultBilling)
        {
            ClearDefaultBilling(now, addressId);
        }

        Touch(now);
        return Result.Success(address);
    }

    public Result RemoveAddress(Guid addressId, DateTimeOffset now)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error;
        }

        var address = FindAddress(addressId);
        if (address is null)
        {
            return CustomerErrors.AddressNotFound;
        }

        _addresses.Remove(address);
        Touch(now);
        return Result.Success();
    }

    public Result<bool> CloseAccount(DateTimeOffset now)
    {
        if (Status == CustomerStatus.Deactivated)
        {
            return Result.Success(false);
        }

        FirstName = null;
        LastName = null;
        Email = null;
        PhoneNumber = null;
        _addresses.Clear();
        Status = CustomerStatus.Deactivated;
        Touch(now);
        return Result.Success(true);
    }

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

    private Result EnsureActive() =>
        Status == CustomerStatus.Active
            ? Result.Success()
            : CustomerErrors.Inactive;

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = LaterOf(UpdatedAt, now);
        Version++;
    }

    private static DateTimeOffset LaterOf(DateTimeOffset current, DateTimeOffset candidate) =>
        candidate > current ? candidate : current;

    private static Result<string> Required(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CustomerErrors.Validation(field, "A value is required.");
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? Result.Success(normalized)
            : CustomerErrors.Validation(field, $"The value cannot exceed {maximumLength} characters.");
    }

    private static Result<string> RequiredOpaque(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CustomerErrors.Validation(field, "A value is required.");
        }

        if (value.Length > maximumLength)
        {
            return CustomerErrors.Validation(field, $"The value cannot exceed {maximumLength} characters.");
        }

        return string.Equals(value, value.Trim(), StringComparison.Ordinal)
            ? Result.Success(value)
            : CustomerErrors.Validation(field, "The value cannot contain leading or trailing whitespace.");
    }

    private static Result<OptionalText> Optional(string? value, string field, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Success(new OptionalText(null));
        }

        var required = Required(value, field, maximumLength);
        return required.IsSuccess
            ? Result.Success(new OptionalText(required.Value))
            : required.Error;
    }

    private static Result<OptionalText> NormalizeEmail(string? value)
    {
        var optional = Optional(value, nameof(Email), 320);
        if (optional.IsFailure || optional.Value.Value is null)
        {
            return optional;
        }

        var normalized = optional.Value.Value.ToLowerInvariant();
        if (!MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerErrors.InvalidEmail;
        }

        return Result.Success(new OptionalText(normalized));
    }

    private readonly record struct OptionalText(string? Value);
}
