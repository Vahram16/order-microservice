using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Order.Api.Domain;

namespace Order.Api.Persistence;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Order> Orders => Set<Domain.Order>();
    internal DbSet<OrderItem> OrderItems => Set<OrderItem>();
    internal DbSet<OrderSubmission> OrderSubmissions => Set<OrderSubmission>();
    internal DbSet<OrderCustomerProjection> OrderCustomers => Set<OrderCustomerProjection>();
    internal DbSet<OrderProductProjection> OrderProducts => Set<OrderProductProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);
        modelBuilder.AddMassTransitOutboxEntities();
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Domain.Order>
{
    public void Configure(EntityTypeBuilder<Domain.Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.CustomerId).IsRequired();
        builder.Property(order => order.PaymentMethodId).IsRequired();
        builder.Property(order => order.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(order => order.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(order => order.TerminalReasonCode).HasMaxLength(64);
        builder.Property(order => order.ExpiresAt).IsRequired();
        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.UpdatedAt).IsRequired();
        builder.Property(order => order.Version).IsConcurrencyToken().IsRequired();
        builder.HasIndex(order => new { order.CustomerId, order.CreatedAt });
        builder.HasIndex(order => new { order.Status, order.ExpiresAt });

        builder.OwnsOne(order => order.ShippingAddress, address =>
        {
            address.Property(value => value.RecipientName).HasColumnName("ShippingRecipientName").HasMaxLength(200).IsRequired();
            address.Property(value => value.Line1).HasColumnName("ShippingLine1").HasMaxLength(200).IsRequired();
            address.Property(value => value.Line2).HasColumnName("ShippingLine2").HasMaxLength(200);
            address.Property(value => value.City).HasColumnName("ShippingCity").HasMaxLength(100).IsRequired();
            address.Property(value => value.Region).HasColumnName("ShippingRegion").HasMaxLength(100);
            address.Property(value => value.PostalCode).HasColumnName("ShippingPostalCode").HasMaxLength(32).IsRequired();
            address.Property(value => value.CountryCode).HasColumnName("ShippingCountryCode").HasMaxLength(2).IsFixedLength().IsRequired();
            address.Property(value => value.PhoneNumber).HasColumnName("ShippingPhoneNumber").HasMaxLength(32);
        });

        builder.HasMany(order => order.Items)
            .WithOne()
            .HasForeignKey(item => item.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.Sku).HasMaxLength(64).IsRequired();
        builder.Property(item => item.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.LineTotal).HasPrecision(18, 2).IsRequired();
        builder.HasIndex(item => new { item.OrderId, item.ProductId }).IsUnique();
    }
}

internal sealed class OrderSubmissionConfiguration : IEntityTypeConfiguration<OrderSubmission>
{
    public void Configure(EntityTypeBuilder<OrderSubmission> builder)
    {
        builder.ToTable("order_submissions");
        builder.HasKey(item => new { item.CustomerId, item.IdempotencyKey }).HasName(OrderDatabaseConstraints.SubmissionPrimaryKey);
        builder.Property(item => item.RequestFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.HasIndex(item => item.OrderId).IsUnique().HasDatabaseName(OrderDatabaseConstraints.SubmissionOrder);
    }
}

internal sealed class OrderCustomerProjectionConfiguration : IEntityTypeConfiguration<OrderCustomerProjection>
{
    public void Configure(EntityTypeBuilder<OrderCustomerProjection> builder)
    {
        builder.ToTable("order_customers");
        builder.HasKey(item => item.CustomerId);
        builder.Property(item => item.CustomerId).ValueGeneratedNever();
        builder.Property(item => item.IdentityProvider).HasMaxLength(32).IsRequired();
        builder.Property(item => item.IdentitySubject).HasMaxLength(255).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.HasIndex(item => new { item.IdentityProvider, item.IdentitySubject })
            .IsUnique()
            .HasDatabaseName(OrderDatabaseConstraints.CustomerIdentity);
    }
}

internal sealed class OrderProductProjectionConfiguration : IEntityTypeConfiguration<OrderProductProjection>
{
    public void Configure(EntityTypeBuilder<OrderProductProjection> builder)
    {
        builder.ToTable("order_products");
        builder.HasKey(item => item.ProductId);
        builder.Property(item => item.ProductId).ValueGeneratedNever();
        builder.Property(item => item.Sku).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(item => item.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(item => item.SourceVersion).IsRequired();
        builder.Property(item => item.IsAvailable).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.Property(item => item.LastSnapshotId);
    }
}
