using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

internal static class UpdateCustomerAddressEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut(
                "/addresses/{addressId:guid}",
                async (
                    Guid addressId,
                    UpdateCustomerAddressRequest request,
                    ClaimsPrincipal principal,
                    HttpRequest httpRequest,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new UpdateCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(httpRequest),
                            addressId,
                            request.ToAddressData()),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("UpdateCurrentCustomerAddress")
            .WithSummary("Replaces an address owned by the current customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.AddressWriteScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}
