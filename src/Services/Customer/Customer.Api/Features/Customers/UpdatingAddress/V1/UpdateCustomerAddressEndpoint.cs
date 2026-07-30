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
                    HttpContext httpContext,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    if (identity.IsFailure)
                    {
                        return CustomerHttpResults.Problem(identity.Error, httpContext);
                    }

                    var expectedVersion = CustomerHttp.ReadExpectedVersion(httpContext.Request);
                    if (expectedVersion.IsFailure)
                    {
                        return CustomerHttpResults.Problem(expectedVersion.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new UpdateCustomerAddressCommand(
                            identity.Value.Provider,
                            identity.Value.Subject,
                            expectedVersion.Value,
                            addressId,
                            request.ToAddressData()),
                        cancellationToken);

                    return result.Match<IResult>(
                        customer =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, customer.Version);
                            return Results.Ok(customer);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("UpdateCurrentCustomerAddress")
            .WithSummary("Replaces an address owned by the current customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.AddressWriteScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
