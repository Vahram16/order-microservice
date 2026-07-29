using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.ClosingAccount.V1;

internal static class CloseCustomerAccountSlice
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete(
                "/",
                async (
                    ClaimsPrincipal principal,
                    HttpRequest request,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new CloseCustomerAccountCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(request)),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("CloseCurrentCustomerAccount")
            .WithSummary("Anonymizes Customer-owned PII, removes saved addresses, and deactivates the customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.DeleteScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}

internal sealed record CloseCustomerAccountCommand(
    string Provider,
    string Subject,
    long ExpectedVersion) : IRequest<CustomerResponse>;

internal sealed class CloseCustomerAccountCommandValidator
    : AbstractValidator<CloseCustomerAccountCommand>
{
    public CloseCustomerAccountCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
    }
}

internal sealed class CloseCustomerAccountCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<CloseCustomerAccountCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        CloseCustomerAccountCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        if (customer.Status == Domain.CustomerStatus.Deactivated)
        {
            return CustomerMappings.ToResponse(customer);
        }

        customer.EnsureExpectedVersion(request.ExpectedVersion);
        var now = timeProvider.GetUtcNow();
        customer.CloseAccount(now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Subject,
            Domain.CustomerAuditActions.AccountClosed,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
