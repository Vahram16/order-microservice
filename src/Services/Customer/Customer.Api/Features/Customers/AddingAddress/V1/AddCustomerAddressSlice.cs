using System.Security.Claims;
using Customer.Api.Domain;
using Customer.Api.Features.Customers.Common;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features.Customers.AddingAddress.V1;

internal static class AddCustomerAddressSlice
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

public sealed record AddCustomerAddressRequest(
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

internal sealed record AddCustomerAddressCommand(
    string Provider,
    string Subject,
    long ExpectedVersion,
    Guid AddressId,
    AddressData Address) : IRequest<AddCustomerAddressResult>;

internal sealed record AddCustomerAddressResult(
    CustomerResponse Customer,
    Guid AddressId,
    bool Created);

internal sealed class AddCustomerAddressCommandValidator
    : AbstractValidator<AddCustomerAddressCommand>
{
    public AddCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.AddressId).NotEmpty();
        RuleFor(command => command.Address).SetValidator(new AddressDataValidator());
    }
}

internal sealed class AddCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<AddCustomerAddressCommand, AddCustomerAddressResult>
{
    public async Task<AddCustomerAddressResult> Handle(
        AddCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ExecuteOnceAsync(request, cancellationToken));
    }

    private async Task<AddCustomerAddressResult> ExecuteOnceAsync(
        AddCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        // A transient retry must start from a clean aggregate and change tracker.
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        var existing = customer.FindAddress(request.AddressId);
        if (existing is not null)
        {
            if (!existing.Matches(request.Address))
            {
                throw new CustomerIdempotencyConflictException(request.AddressId);
            }

            return new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                existing.Id,
                false);
        }

        customer.EnsureExpectedVersion(request.ExpectedVersion);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await CustomerPersistence.ClearCompetingDefaultsAsync(
            dbContext,
            customer.Id,
            request.AddressId,
            request.Address,
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        customer.AddAddress(request.AddressId, request.Address, now);
        CustomerPersistence.AddAudit(
            dbContext,
            customer,
            request.Subject,
            CustomerAuditActions.AddressAdded,
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new AddCustomerAddressResult(
                CustomerMappings.ToResponse(customer),
                request.AddressId,
                true);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(request, cancellationToken);
        }
        catch (DbUpdateException exception) when (
            CustomerPersistence.IsUniqueConstraintViolation(
                exception,
                CustomerConstraintNames.AddressPrimaryKey))
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReloadIdempotentResultAsync(request, cancellationToken);
        }
    }

    private async Task<AddCustomerAddressResult> ReloadIdempotentResultAsync(
        AddCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var customer = await CustomerPersistence.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);
        var address = customer.FindAddress(request.AddressId);
        if (address is null || !address.Matches(request.Address))
        {
            throw new CustomerVersionMismatchException(
                request.ExpectedVersion,
                customer.Version);
        }

        return new AddCustomerAddressResult(
            CustomerMappings.ToResponse(customer),
            address.Id,
            false);
    }
}

internal sealed class AddressDataValidator : AbstractValidator<AddressData>
{
    public AddressDataValidator()
    {
        RuleFor(address => address.Label).MaximumLength(50);
        RuleFor(address => address.RecipientName).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line1).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line2).MaximumLength(200);
        RuleFor(address => address.City).NotEmpty().MaximumLength(100);
        RuleFor(address => address.Region).MaximumLength(100);
        RuleFor(address => address.PostalCode).NotEmpty().MaximumLength(32);
        RuleFor(address => address.CountryCode)
            .NotEmpty()
            .Length(2)
            .Matches("^[A-Za-z]{2}$")
            .WithMessage("CountryCode must be an ISO 3166-1 alpha-2 code.");
        RuleFor(address => address.PhoneNumber).MaximumLength(32);
    }
}
