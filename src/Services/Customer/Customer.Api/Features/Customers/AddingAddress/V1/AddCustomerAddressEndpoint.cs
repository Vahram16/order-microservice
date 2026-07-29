using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal static class AddCustomerAddressEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost(
                "/addresses",
                async (
                    AddCustomerAddressRequest request,
                    ClaimsPrincipal principal,
                    HttpRequest httpRequest,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var result = await sender.Send(
                        new AddCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(httpRequest),
                            CustomerHttp.RequireIdempotencyKey(httpRequest),
                            request.ToAddressData()),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, result.Customer.Version);
                    return result.Created
                        ? Results.Created(
                            $"/api/v1/customers/me/addresses/{result.AddressId}",
                            result.Customer)
                        : Results.Ok(result.Customer);
                })
            .WithName("AddCurrentCustomerAddress")
            .WithSummary("Idempotently adds a saved address to the current customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.AddressWriteScope))
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}
