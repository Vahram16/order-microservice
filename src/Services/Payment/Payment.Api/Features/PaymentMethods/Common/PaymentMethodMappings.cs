using Payment.Api.Domain;
namespace Payment.Api.Features.PaymentMethods.Common;

internal static class PaymentMethodMappings { public static PaymentMethodResponse ToResponse(PaymentMethod method) => new(method.Id, method.Brand, method.Last4, method.ExpMonth, method.ExpYear, method.WalletType, method.IsDefault, method.Status.ToString()); }
