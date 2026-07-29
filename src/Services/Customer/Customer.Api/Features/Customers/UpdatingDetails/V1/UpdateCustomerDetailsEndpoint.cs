using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal static class UpdateCustomerDetailsEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut(
                "/details",
                async (
                    UpdateCustomerDetailsRequest request,
                    ClaimsPrincipal principal,
                    HttpRequest httpRequest,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new UpdateCustomerDetailsCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(httpRequest),
                            request.FirstName,
                            request.LastName,
                            request.Email,
                            request.PhoneNumber),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("UpdateCurrentCustomerDetails")
            .WithSummary("Replaces the current customer's business-owned contact details.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.UpdateScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}
