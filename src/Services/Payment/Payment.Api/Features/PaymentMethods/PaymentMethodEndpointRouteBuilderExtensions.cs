using Payment.Api.Features.PaymentMethods.CreatingSetup.V1;
using Payment.Api.Features.PaymentMethods.Listing.V1;
using Payment.Api.Features.PaymentMethods.SettingDefault.V1;

namespace Payment.Api.Features.PaymentMethods;

internal static class PaymentMethodEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapPaymentMethodEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/payment-methods").WithTags("Payment Methods");
        CreatePaymentMethodSetupEndpoint.Map(group);
        ListPaymentMethodsEndpoint.Map(group);
        SetDefaultPaymentMethodEndpoint.Map(group);
        return endpoints;
    }
}
