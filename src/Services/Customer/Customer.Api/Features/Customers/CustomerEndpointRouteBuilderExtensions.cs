using Customer.Api.Features.Customers.AddingAddress.V1;
using Customer.Api.Features.Customers.ClosingAccount.V1;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Features.Customers.Exporting.V1;
using Customer.Api.Features.Customers.GettingCurrent.V1;
using Customer.Api.Features.Customers.Provisioning.V1;
using Customer.Api.Features.Customers.RemovingAddress.V1;
using Customer.Api.Features.Customers.UpdatingAddress.V1;
using Customer.Api.Features.Customers.UpdatingDetails.V1;

namespace Customer.Api.Features.Customers;

internal static class CustomerEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers/me")
            .WithTags("Customers")
            .AddEndpointFilter(CustomerHttp.AddSensitiveResponseHeadersAsync);

        ProvisionCustomerEndpoint.Map(group);
        GetCurrentCustomerEndpoint.Map(group);
        UpdateCustomerDetailsEndpoint.Map(group);
        AddCustomerAddressEndpoint.Map(group);
        UpdateCustomerAddressEndpoint.Map(group);
        RemoveCustomerAddressEndpoint.Map(group);
        ExportCustomerEndpoint.Map(group);
        CloseCustomerAccountEndpoint.Map(group);

        return endpoints;
    }
}
