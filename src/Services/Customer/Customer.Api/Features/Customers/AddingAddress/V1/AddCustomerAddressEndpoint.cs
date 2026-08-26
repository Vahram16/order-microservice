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

                    var idempotencyKey = CustomerHttp.ReadIdempotencyKey(httpContext.Request);
                    if (idempotencyKey.IsFailure)
                    {
                        return CustomerHttpResults.Problem(idempotencyKey.Error, httpContext);
                    }

                    var result = await sender.Send(
                        new AddCustomerAddressCommand(
                            identity.Value.Provider,
                            identity.Value.Subject,
                            expectedVersion.Value,
                            idempotencyKey.Value,
                            request.ToAddressData()),
                        cancellationToken);

                    return result.Match<IResult>(
                        success =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, success.Customer.Version);
                            return success.Created
                                ? Results.Created(
                                    $"/api/v1/customers/me/addresses/{success.AddressId}",
                                    success.Customer)
                                : Results.Ok(success.Customer);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("AddCurrentCustomerAddress")
            .WithSummary("Idempotently adds a saved address to the current customer.")
            .RequireAuthorization(RolePolicy.For(CustomerAuthorization.AddressWriteRole))
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
