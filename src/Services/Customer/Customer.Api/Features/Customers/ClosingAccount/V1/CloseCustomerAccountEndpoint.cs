using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal static class CloseCustomerAccountEndpoint
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete(
                "/",
                async (
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
                        new CloseCustomerAccountCommand(
                            identity.Value.Provider,
                            identity.Value.Subject,
                            expectedVersion.Value),
                        cancellationToken);

                    return result.Match<IResult>(
                        customer =>
                        {
                            CustomerHttp.WriteEtag(httpContext.Response, customer.Version);
                            return Results.Ok(customer);
                        },
                        error => CustomerHttpResults.Problem(error, httpContext));
                })
            .WithName("CloseCurrentCustomerAccount")
            .WithSummary("Anonymizes Customer-owned PII, removes saved addresses, and deactivates the customer.")
            .RequireAuthorization(RolePolicy.For(CustomerAuthorization.DeleteRole))
            .Produces<CustomerResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired);
}
