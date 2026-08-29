using Inventory.Api.Domain;
using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Api.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    internal DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();
    internal DbSet<InventoryReservationLine> ReservationLines => Set<InventoryReservationLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        modelBuilder.AddMassTransitOutboxEntities();
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");
        builder.HasKey(item => item.ProductId);
        builder.Property(item => item.ProductId).ValueGeneratedNever();
        builder.Property(item => item.OnHand).IsRequired();
        builder.Property(item => item.Reserved).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
    }
}

internal sealed class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("inventory_reservations");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.OrderId).IsRequired();
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(item => item.ReasonCode).HasMaxLength(64);
        builder.Property(item => item.ExpiresAt).IsRequired();
        builder.Property(item => item.CreatedAt).IsRequired();
        builder.Property(item => item.UpdatedAt).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
        builder.HasIndex(item => item.OrderId).IsUnique().HasDatabaseName(InventoryDatabaseConstraints.ReservationOrder);
        builder.HasIndex(item => new { item.Status, item.ExpiresAt });
        builder.HasMany(item => item.Lines)
            .WithOne()
            .HasForeignKey(line => line.ReservationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(item => item.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InventoryReservationLineConfiguration : IEntityTypeConfiguration<InventoryReservationLine>
{
    public void Configure(EntityTypeBuilder<InventoryReservationLine> builder)
    {
        builder.ToTable("inventory_reservation_lines");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ReservationId).IsRequired();
        builder.Property(item => item.ProductId).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.HasIndex(item => new { item.ReservationId, item.ProductId }).IsUnique();
    }
}
