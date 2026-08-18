using Microservices.Application;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure;
namespace Payment.Api.Features.PaymentMethods.SettingDefault.V1;

internal sealed record SetDefaultPaymentMethodCommand(CurrentPaymentIdentity Identity, Guid PaymentMethodId) : ICommand<Result<PaymentMethodResponse>>;
