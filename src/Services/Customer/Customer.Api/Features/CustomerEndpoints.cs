using System.Security.Claims;
using Customer.Api.Domain;
using Customer.Api.Infrastructure;
using Customer.Api.Persistence;
using FluentValidation;
using MediatR;
using Microservices.Security;
using Microsoft.EntityFrameworkCore;

namespace Customer.Api.Features;

internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/customers/me")
            .WithTags("Customers")
            .RequireAuthorization(RolePolicy.For("order-user"));

        group.MapPut(
                "/",
                async (
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var result = await sender.Send(
                        new ProvisionCustomerCommand(identity),
                        cancellationToken);

                    return result.Created
                        ? Results.Created("/api/v1/customers/me", result.Customer)
                        : Results.Ok(result.Customer);
                })
            .WithName("ProvisionCurrentCustomer")
            .WithSummary("Idempotently provisions the customer bound to the current Keycloak subject.")
            .Produces<CustomerResponse>(StatusCodes.Status200OK)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet(
                "/",
                async (
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new GetCurrentCustomerQuery(identity.Provider, identity.Subject),
                        cancellationToken);

                    return customer is null ? Results.NotFound() : Results.Ok(customer);
                })
            .WithName("GetCurrentCustomer")
            .WithSummary("Gets the customer bound to the current Keycloak subject.")
            .Produces<CustomerResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut(
                "/details",
                async (
                    UpdateCustomerDetailsRequest request,
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var customer = await sender.Send(
                        new UpdateCustomerDetailsCommand(
                            identity.Provider,
                            identity.Subject,
                            request.FirstName,
                            request.LastName,
                            request.Email,
                            request.PhoneNumber),
                        cancellationToken);

                    return Results.Ok(customer);
                })
            .WithName("UpdateCurrentCustomerDetails")
            .WithSummary("Replaces the current customer's business-owned contact details.")
            .Produces<CustomerResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost(
                "/addresses",
                async (
                    UpsertCustomerAddressRequest request,
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var address = await sender.Send(
                        new AddCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            request.ToAddressData()),
                        cancellationToken);

                    return Results.Created(
                        $"/api/v1/customers/me/addresses/{address.Id}",
                        address);
                })
            .WithName("AddCurrentCustomerAddress")
            .WithSummary("Adds a saved address to the current customer.")
            .Produces<CustomerAddressResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut(
                "/addresses/{addressId:guid}",
                async (
                    Guid addressId,
                    UpsertCustomerAddressRequest request,
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    var address = await sender.Send(
                        new UpdateCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            addressId,
                            request.ToAddressData()),
                        cancellationToken);

                    return Results.Ok(address);
                })
            .WithName("UpdateCurrentCustomerAddress")
            .WithSummary("Updates an address owned by the current customer.")
            .Produces<CustomerAddressResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete(
                "/addresses/{addressId:guid}",
                async (
                    Guid addressId,
                    ClaimsPrincipal principal,
                    ISender sender,
                    CancellationToken cancellationToken) =>
                {
                    var identity = CurrentIdentity.From(principal);
                    await sender.Send(
                        new RemoveCustomerAddressCommand(
                            identity.Provider,
                            identity.Subject,
                            addressId),
                        cancellationToken);

                    return Results.NoContent();
                })
            .WithName("RemoveCurrentCustomerAddress")
            .WithSummary("Removes an address owned by the current customer.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }
}

internal sealed record ProvisionCustomerCommand(CurrentIdentity Identity)
    : IRequest<ProvisionCustomerResult>;

internal sealed record ProvisionCustomerResult(CustomerResponse Customer, bool Created);

internal sealed class ProvisionCustomerCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<ProvisionCustomerCommand, ProvisionCustomerResult>
{
    public async Task<ProvisionCustomerResult> Handle(
        ProvisionCustomerCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(
            request.Identity.Provider,
            request.Identity.Subject,
            cancellationToken);
        if (existing is not null)
        {
            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }

        var customer = Domain.Customer.Register(
            request.Identity.Provider,
            request.Identity.Subject,
            request.Identity.GivenName,
            request.Identity.FamilyName,
            request.Identity.Email,
            timeProvider.GetUtcNow());

        dbContext.Customers.Add(customer);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ProvisionCustomerResult(CustomerMappings.ToResponse(customer), true);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await FindAsync(
                request.Identity.Provider,
                request.Identity.Subject,
                cancellationToken);

            if (existing is null)
            {
                throw;
            }

            return new ProvisionCustomerResult(CustomerMappings.ToResponse(existing), false);
        }
    }

    private Task<Domain.Customer?> FindAsync(
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        dbContext.Customers
            .Include(customer => customer.Addresses)
            .SingleOrDefaultAsync(
                customer =>
                    customer.IdentityProvider == provider &&
                    customer.IdentitySubject == subject,
                cancellationToken);
}

internal sealed record GetCurrentCustomerQuery(string Provider, string Subject)
    : IRequest<CustomerResponse?>;

internal sealed class GetCurrentCustomerQueryHandler(CustomerDbContext dbContext)
    : IRequestHandler<GetCurrentCustomerQuery, CustomerResponse?>
{
    public async Task<CustomerResponse?> Handle(
        GetCurrentCustomerQuery request,
        CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .AsNoTracking()
            .Include(entity => entity.Addresses)
            .SingleOrDefaultAsync(
                entity =>
                    entity.IdentityProvider == request.Provider &&
                    entity.IdentitySubject == request.Subject,
                cancellationToken);

        return customer is null ? null : CustomerMappings.ToResponse(customer);
    }
}

internal sealed record UpdateCustomerDetailsCommand(
    string Provider,
    string Subject,
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
        var customer = await CustomerQueries.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        customer.UpdateDetails(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return CustomerMappings.ToResponse(customer);
    }
}

internal sealed record AddCustomerAddressCommand(
    string Provider,
    string Subject,
    AddressData Address) : IRequest<CustomerAddressResponse>;

internal sealed record UpdateCustomerAddressCommand(
    string Provider,
    string Subject,
    Guid AddressId,
    AddressData Address) : IRequest<CustomerAddressResponse>;

internal sealed record RemoveCustomerAddressCommand(
    string Provider,
    string Subject,
    Guid AddressId) : IRequest;

internal sealed class AddCustomerAddressCommandValidator
    : AbstractValidator<AddCustomerAddressCommand>
{
    public AddCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.Address).SetValidator(new AddressDataValidator());
    }
}

internal sealed class UpdateCustomerAddressCommandValidator
    : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.AddressId).NotEmpty();
        RuleFor(command => command.Address).SetValidator(new AddressDataValidator());
    }
}

internal sealed class RemoveCustomerAddressCommandValidator
    : AbstractValidator<RemoveCustomerAddressCommand>
{
    public RemoveCustomerAddressCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.Subject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.AddressId).NotEmpty();
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

internal sealed class AddCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<AddCustomerAddressCommand, CustomerAddressResponse>
{
    public async Task<CustomerAddressResponse> Handle(
        AddCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerQueries.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        var address = customer.AddAddress(request.Address, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return CustomerMappings.ToResponse(address);
    }
}

internal sealed class UpdateCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateCustomerAddressCommand, CustomerAddressResponse>
{
    public async Task<CustomerAddressResponse> Handle(
        UpdateCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerQueries.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        var address = customer.UpdateAddress(
            request.AddressId,
            request.Address,
            timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return CustomerMappings.ToResponse(address);
    }
}

internal sealed class RemoveCustomerAddressCommandHandler(
    CustomerDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<RemoveCustomerAddressCommand>
{
    public async Task Handle(
        RemoveCustomerAddressCommand request,
        CancellationToken cancellationToken)
    {
        var customer = await CustomerQueries.LoadRequiredAsync(
            dbContext,
            request.Provider,
            request.Subject,
            cancellationToken);

        customer.RemoveAddress(request.AddressId, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

internal static class CustomerQueries
{
    public static async Task<Domain.Customer> LoadRequiredAsync(
        CustomerDbContext dbContext,
        string provider,
        string subject,
        CancellationToken cancellationToken) =>
        await dbContext.Customers
            .Include(customer => customer.Addresses)
            .SingleOrDefaultAsync(
                customer =>
                    customer.IdentityProvider == provider &&
                    customer.IdentitySubject == subject,
                cancellationToken)
        ?? throw new CustomerNotFoundException();
}

internal static class CustomerMappings
{
    public static CustomerResponse ToResponse(Domain.Customer customer) => new(
        customer.Id,
        customer.FirstName,
        customer.LastName,
        customer.Email,
        customer.PhoneNumber,
        customer.Status.ToString(),
        customer.Addresses
            .OrderByDescending(address => address.IsDefaultShipping)
            .ThenByDescending(address => address.IsDefaultBilling)
            .ThenBy(address => address.CreatedAt)
            .Select(ToResponse)
            .ToArray(),
        customer.CreatedAt,
        customer.UpdatedAt,
        customer.Version);

    public static CustomerAddressResponse ToResponse(CustomerAddress address) => new(
        address.Id,
        address.Label,
        address.RecipientName,
        address.Line1,
        address.Line2,
        address.City,
        address.Region,
        address.PostalCode,
        address.CountryCode,
        address.PhoneNumber,
        address.IsDefaultShipping,
        address.IsDefaultBilling,
        address.CreatedAt,
        address.UpdatedAt);
}

public sealed record UpdateCustomerDetailsRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber);

public sealed record UpsertCustomerAddressRequest(
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

public sealed record CustomerResponse(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? PhoneNumber,
    string Status,
    IReadOnlyCollection<CustomerAddressResponse> Addresses,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record CustomerAddressResponse(
    Guid Id,
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
    bool IsDefaultBilling,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
