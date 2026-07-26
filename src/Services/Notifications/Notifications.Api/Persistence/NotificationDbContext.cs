using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Notifications.Api.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("data_protection_keys");
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.Property(delivery => delivery.Source).HasMaxLength(100);
            entity.Property(delivery => delivery.IdempotencyKey).HasMaxLength(128);
            entity.Property(delivery => delivery.Template).HasMaxLength(100);
            entity.Property(delivery => delivery.ProtectedPayload).HasColumnType("text");
            entity.Property(delivery => delivery.PayloadHash).HasMaxLength(64);
            entity.Property(delivery => delivery.ProviderMessageId).HasMaxLength(100);
            entity.Property(delivery => delivery.LastError).HasMaxLength(256);
            entity.HasIndex(delivery => new { delivery.Source, delivery.SourceEventId })
                .IsUnique()
                .HasDatabaseName("ux_deliveries_source_event");
            entity.HasIndex(delivery => delivery.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("ux_deliveries_idempotency_key");
            entity.HasIndex(delivery => new
                {
                    delivery.AcceptedByProviderAtUtc,
                    delivery.DeadLetteredAtUtc,
                    delivery.AvailableAtUtc
                })
                .HasDatabaseName("ix_deliveries_dispatch");
        });
    }
}
