using Microservices.Application;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed record CreatePaymentMethodSetupCommand(
    string IdentityProvider,
    string IdentitySubject,
    Guid RequestId,
    bool MakeDefault) : ICommand<Result<CreatePaymentMethodSetupResult>>;
