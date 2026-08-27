using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microservices.Application;
using Microservices.Application.Messaging;
using Microservices.Contracts.Inventory.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Order.Api.Domain;
using Order.Api.Features.Orders.Common;
using Order.Api.Persistence;

namespace Order.Api.Features.Orders.Creating.V1;

internal sealed class CreateOrderHandler(
    OrderDbContext dbContext,
    IIntegrationCommandSender<ReserveInventory> inventorySender,
    IOptions<OrderWorkflowOptions> workflowOptions,
    TimeProvider timeProvider)
    : ICommandHandler<CreateOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(
        CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var referenceDataReady = await dbContext.Set<OrderReferenceDataSynchronization>()
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.Id == OrderReferenceDataSynchronization.SingletonId &&
                    item.ReadyAt != null,
                cancellationToken);
        if (!referenceDataReady)
        {
            return OrderApplicationErrors.ReferenceDataSynchronizing;
        }

        var customer = await dbContext.OrderCustomers.SingleOrDefaultAsync(
            item =>
                item.IdentityProvider == command.IdentityProvider &&
                item.IdentitySubject == command.IdentitySubject,
            cancellationToken);
        if (customer is null)
        {
            return OrderApplicationErrors.CustomerNotSynchronized;
        }

        var fingerprint = CreateFingerprint(command);
        var existingSubmission = await dbContext.OrderSubmissions.FindAsync(
            [customer.CustomerId, command.IdempotencyKey],
            cancellationToken);
        if (existingSubmission is not null)
        {
            return await ResolveExistingAsync(existingSubmission, fingerprint, cancellationToken);
        }

        var productIds = command.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToArray();
        var products = await dbContext.OrderProducts
            .Where(item => productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        if (products.Count != productIds.Length)
        {
            return OrderApplicationErrors.CatalogNotSynchronized;
        }

        if (products.Values.Any(item => !item.IsAvailable))
        {
            return OrderApplicationErrors.ProductUnavailable;
        }

        var drafts = command.Items.Select(item =>
        {
            var product = products[item.ProductId];
            return new OrderItemDraft(
                product.ProductId,
                product.Sku,
                product.Name,
                item.Quantity,
                product.Price,
                product.CurrencyCode);
        }).ToArray();

        var now = timeProvider.GetUtcNow();
        var expiresAt = now + workflowOptions.Value.CheckoutTimeout;
        var creation = Domain.Order.Place(
            Guid.NewGuid(),
            customer.CustomerId,
            command.PaymentMethodId,
            drafts,
            new ShippingAddressData(
                command.ShippingAddress.RecipientName,
                command.ShippingAddress.Line1,
                command.ShippingAddress.Line2,
                command.ShippingAddress.City,
                command.ShippingAddress.Region,
                command.ShippingAddress.PostalCode,
                command.ShippingAddress.CountryCode,
                command.ShippingAddress.PhoneNumber),
            expiresAt,
            now);
        if (creation.IsFailure)
        {
            return creation.Error;
        }

        var order = creation.Value;
        dbContext.Orders.Add(order);
        dbContext.OrderSubmissions.Add(OrderSubmission.Create(
            customer.CustomerId,
            command.IdempotencyKey,
            fingerprint,
            order.Id,
            now));

        await inventorySender.SendAsync(
            new ReserveInventory(
                order.Id,
                order.Items
                    .Select(item => new InventoryReservationItem(item.ProductId, item.Quantity))
                    .ToArray(),
                order.ExpiresAt),
            new IntegrationMessageMetadata(
                MessageId: order.Id,
                CorrelationId: order.Id),
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.IsUniqueConstraintViolation(
                OrderDatabaseConstraints.SubmissionPrimaryKey))
        {
            dbContext.ChangeTracker.Clear();
            existingSubmission = await dbContext.OrderSubmissions.FindAsync(
                [customer.CustomerId, command.IdempotencyKey],
                cancellationToken);
            if (existingSubmission is null)
            {
                throw;
            }

            return await ResolveExistingAsync(
                existingSubmission,
                fingerprint,
                cancellationToken);
        }

        return Result.Success(OrderMappings.ToResponse(order));
    }

    private async Task<Result<OrderResponse>> ResolveExistingAsync(
        OrderSubmission submission,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                submission.RequestFingerprint,
                fingerprint,
                StringComparison.Ordinal))
        {
            return OrderApplicationErrors.IdempotencyKeyReused;
        }

        var order = await dbContext.Orders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == submission.OrderId, cancellationToken);
        return Result.Success(OrderMappings.ToResponse(order));
    }

    private static string CreateFingerprint(CreateOrderCommand command)
    {
        var builder = new StringBuilder();
        builder.Append(command.PaymentMethodId.ToString("N", CultureInfo.InvariantCulture));
        foreach (var item in command.Items.OrderBy(item => item.ProductId))
        {
            builder.Append('|')
                .Append(item.ProductId.ToString("N", CultureInfo.InvariantCulture))
                .Append(':')
                .Append(item.Quantity.ToString(CultureInfo.InvariantCulture));
        }

        Append(builder, command.ShippingAddress.RecipientName);
        Append(builder, command.ShippingAddress.Line1);
        Append(builder, command.ShippingAddress.Line2);
        Append(builder, command.ShippingAddress.City);
        Append(builder, command.ShippingAddress.Region);
        Append(builder, command.ShippingAddress.PostalCode);
        Append(builder, command.ShippingAddress.CountryCode?.ToUpperInvariant());
        Append(builder, command.ShippingAddress.PhoneNumber);

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string? value)
    {
        var normalized = value?.Trim();
        builder.Append('|')
            .Append(normalized?.Length ?? 0)
            .Append(':')
            .Append(normalized);
    }
}
