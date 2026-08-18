using Microservices.Application;
using Payment.Api.Infrastructure;

namespace Payment.Api.Features.PaymentMethods.CreatingSetup.V1;

internal sealed record CreatePaymentMethodSetupCommand(
    CurrentPaymentIdentity Identity,
    Guid RequestId) : ICommand<Result<CreatePaymentMethodSetupResult>>;
