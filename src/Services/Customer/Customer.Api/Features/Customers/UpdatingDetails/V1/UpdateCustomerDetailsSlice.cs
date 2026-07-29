using System.Security.Claims;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;

namespace Customer.Api.Features.Customers.UpdatingDetails.V1;

internal static class UpdateCustomerDetailsSlice
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

public sealed record UpdateCustomerDetailsRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber);

internal sealed record UpdateCustomerDetailsCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber) : IRequest<CustomerResponse>;

internal sealed class UpdateCustomerDetailsCommandValidator
    : AbstractValidator<UpdateCustomerDetailsCommand>
{
    public UpdateCustomerDetailsCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.FirstName).MaximumLength(100);
        RuleFor(command => command.LastName).MaximumLength(100);
        RuleFor(command => command.Email)
            .EmailAddress()
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.PhoneNumber).MaximumLength(32);
    }
}

internal sealed class UpdateCustomerDetailsCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateCustomerDetailsCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        UpdateCustomerDetailsCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(request.ExpectedVersion);

        var now = timeProvider.GetUtcNow();
        customer.UpdateDetails(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Subject,
            Domain.CustomerAuditActions.DetailsUpdated,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
