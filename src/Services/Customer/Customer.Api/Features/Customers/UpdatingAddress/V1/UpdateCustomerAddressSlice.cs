using System.Security.Claims;
using Customer.Api.Domain;
using Customer.Api.Features.Customers.AddingAddress.V1;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.UpdatingAddress.V1;

internal static class UpdateCustomerAddressSlice
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

public sealed record UpdateCustomerAddressRequest(
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? PhoneNumber,
    bool IsDefaultShipping,
    bool IsDefaultBilling)
{
    internal AddressData ToAddressData() => new(
        Label,
        RecipientName,
        Line1,
        Line2,
        City,
        Region,
        PostalCode,
        CountryCode,
        PhoneNumber,
        IsDefaultShipping,
        IsDefaultBilling);
}

internal sealed record UpdateCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId,
    AddressData Address) : IRequest<CustomerResponse>;

internal sealed class UpdateCustomerAddressCommandValidator
    : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.AddressId).NotEmpty();
        RuleFor(command => command.Address).SetValidator(new AddressDataValidator());
    }
}

internal sealed class UpdateCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateCustomerAddressCommand, CustomerResponse>
{
    public async Task<CustomerResponse> Handle(
        UpdateCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(request, cancellationToken));
    }

    private async Task<CustomerResponse> ExecuteOnceAsync(
        UpdateCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);
        customer.EnsureExpectedVersion(request.ExpectedVersion);
        _ = customer.FindAddress(request.AddressId)
            ?? throw new CustomerAddressNotFoundException(request.AddressId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await CustomerPersistence.ClearCompetingDefaultsAsync(
            dbContext,
            customer.Id,
            request.AddressId,
            request.Address,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        customer.UpdateAddress(request.AddressId, request.Address, now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Subject,
            CustomerAuditActions.AddressUpdated,
            now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}
