using Microservices.Primitives;

namespace Payment.Api.Domain;

public sealed class PaymentCustomer
{
    private PaymentCustomer() { }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IdentityProvider { get; private set; } = string.Empty;
    public string IdentitySubject { get; private set; } = string.Empty;
    public string? ProviderCustomerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    public static Result<PaymentCustomer> Create(
        Guid id,
        Guid customerId,
        string identityProvider,
        string identitySubject,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
        {
            return PaymentErrors.InvalidPaymentCustomerId;
        }

        if (customerId == Guid.Empty)
        {
            return PaymentErrors.InvalidCustomerId;
        }

        var identityValidation = ValidateIdentity(identityProvider, identitySubject);
        if (identityValidation.IsFailure)
        {
            return identityValidation.Error;
        }

        return Result.Success(new PaymentCustomer
        {
            Id = id,
            CustomerId = customerId,
            IdentityProvider = identityProvider,
            IdentitySubject = identitySubject,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        });
    }

    public Result EnsureCustomerIdentity(
        Guid customerId,
        string identityProvider,
        string identitySubject)
    {
        if (customerId == Guid.Empty)
        {
            return PaymentErrors.InvalidCustomerId;
        }

        var identityValidation = ValidateIdentity(identityProvider, identitySubject);
        if (identityValidation.IsFailure)
        {
            return identityValidation.Error;
        }

        if (CustomerId != customerId ||
            !string.Equals(IdentityProvider, identityProvider, StringComparison.Ordinal) ||
            !string.Equals(IdentitySubject, identitySubject, StringComparison.Ordinal))
        {
            return PaymentErrors.CustomerIdentityConflict;
        }

        return Result.Success();
    }

    public Result AssignProviderCustomer(string providerCustomerId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerCustomerId) ||
            !string.Equals(providerCustomerId, providerCustomerId.Trim(), StringComparison.Ordinal) ||
            providerCustomerId.Length > 255)
        {
            return PaymentErrors.Validation(
                nameof(providerCustomerId),
                "Provider customer identifier is invalid.");
        }

        if (ProviderCustomerId is not null)
        {
            return string.Equals(ProviderCustomerId, providerCustomerId, StringComparison.Ordinal)
                ? Result.Success()
                : PaymentErrors.ProviderCustomerConflict;
        }

        ProviderCustomerId = providerCustomerId;
        UpdatedAt = now;
        Version++;
        return Result.Success();
    }

    private static Result ValidateIdentity(string identityProvider, string identitySubject)
    {
        if (string.IsNullOrWhiteSpace(identityProvider) ||
            !string.Equals(identityProvider, identityProvider.Trim(), StringComparison.Ordinal) ||
            identityProvider.Length > 32)
        {
            return PaymentErrors.Validation(
                nameof(identityProvider),
                "Identity provider is invalid.");
        }

        if (string.IsNullOrWhiteSpace(identitySubject) ||
            !string.Equals(identitySubject, identitySubject.Trim(), StringComparison.Ordinal) ||
            identitySubject.Length > 255)
        {
            return PaymentErrors.Validation(
                nameof(identitySubject),
                "Identity subject is invalid.");
        }

        return Result.Success();
    }
}
