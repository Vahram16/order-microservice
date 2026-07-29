using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.RemovingAddress.V1;

internal static class RemoveCustomerAddressSlice
{
    public static void Map(IEndpointRouteBuilder group) =>
        group.MapDelete(
                "/addresses/{addressId:guid}",
                async (
                    Guid addressId,
                    ClaimsPrincipal principal,
                    HttpRequest httpRequest,
                    HttpResponse response,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new RemoveCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            CustomerHttp.RequireExpectedVersion(httpRequest),
                            addressId),
                        cancellationToken);

                    CustomerHttp.WriteEtag(response, customer.Version);
                    return Results.Ok(customer);
                })
            .WithName("RemoveCurrentCustomerAddress")
            .WithSummary("Removes an address owned by the current customer.")
            .RequireAuthorization(
                RolePolicy.For(CustomerAuthorization.Role),
                ScopePolicy.For(CustomerAuthorization.AddressWriteScope))
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status428PreconditionRequired);
}

internal sealed record RemoveCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId) : IRequest<CustomerResponse>;

internal sealed class RemoveCustomerAddressCommandValidator
    : AbstractValidator<RemoveCustomerAddressCommand>
{
    public RemoveCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.AddressId).NotEmpty();
    }
}

internal sealed class RemoveCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<RemoveCustomerAddressCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        RemoveCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(request.ExpectedVersion);

        var now = timeProvider.GetUtcNow();
        customer.RemoveAddress(request.AddressId, now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Subject,
            Domain.CustomerAuditActions.AddressRemoved,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
