using MassTransit;
using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;

namespace Payment.Api.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    internal DbSet<PaymentCustomer> PaymentCustomers => Set<PaymentCustomer>();
    internal DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    internal DbSet<PaymentMethodSetupOperation> PaymentMethodSetupOperations => Set<PaymentMethodSetupOperation>();
    internal DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        ConfigurePaymentCustomer(modelBuilder);
        ConfigurePaymentMethod(modelBuilder);
        ConfigurePaymentMethodSetup(modelBuilder);
        ConfigureWebhookEvent(modelBuilder);
        modelBuilder.AddMassTransitOutboxEntities();
    }

    private static void ConfigurePaymentCustomer(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PaymentCustomer>();
        entity.ToTable("payment_customers");
        entity.HasKey(customer => customer.Id);
        entity.Property(customer => customer.CustomerId).IsRequired();
        entity.Property(customer => customer.IdentityProvider).HasMaxLength(32).IsRequired();
        entity.Property(customer => customer.IdentitySubject).HasMaxLength(255).IsRequired();
        entity.Property(customer => customer.ProviderCustomerId).HasMaxLength(255);
        entity.Property(customer => customer.CreatedAt).IsRequired();
        entity.Property(customer => customer.UpdatedAt).IsRequired();
        entity.Property(customer => customer.Version).IsConcurrencyToken();

        entity.HasIndex(customer => customer.CustomerId)
            .IsUnique()
            .HasDatabaseName(PaymentDatabaseConstraints.CustomerId);
        entity.HasIndex(customer => new { customer.IdentityProvider, customer.IdentitySubject })
            .IsUnique()
            .HasDatabaseName(PaymentDatabaseConstraints.CustomerIdentity);
        entity.HasIndex(customer => customer.ProviderCustomerId)
            .IsUnique()
            .HasFilter("\"ProviderCustomerId\" IS NOT NULL")
            .HasDatabaseName(PaymentDatabaseConstraints.ProviderCustomer);
    }

    private static void ConfigurePaymentMethod(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PaymentMethod>();
        entity.ToTable("payment_methods");
        entity.HasKey(method => method.Id);
        entity.Property(method => method.ProviderPaymentMethodId).HasMaxLength(255).IsRequired();
        entity.Property(method => method.Brand).HasMaxLength(32).IsRequired();
        entity.Property(method => method.Last4).HasMaxLength(4).IsRequired();
        entity.Property(method => method.WalletType).HasMaxLength(32);
        entity.Property(method => method.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        entity.Property(method => method.CreatedAt).IsRequired();
        entity.Property(method => method.UpdatedAt).IsRequired();

        entity.HasIndex(method => method.ProviderPaymentMethodId)
            .IsUnique()
            .HasDatabaseName(PaymentDatabaseConstraints.ProviderPaymentMethod);
        entity.HasIndex(method => new { method.PaymentCustomerId, method.IsDefault })
            .IsUnique()
            .HasFilter("\"IsDefault\"")
            .HasDatabaseName(PaymentDatabaseConstraints.DefaultPaymentMethod);
        entity.HasIndex(method => method.PaymentCustomerId);

        entity.HasOne<PaymentCustomer>()
            .WithMany()
            .HasForeignKey(method => method.PaymentCustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePaymentMethodSetup(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PaymentMethodSetupOperation>();
        entity.ToTable("payment_method_setups");
        entity.HasKey(operation => operation.Id)
            .HasName(PaymentDatabaseConstraints.PaymentMethodSetupPrimaryKey);
        entity.Property(operation => operation.ProviderSetupIntentId).HasMaxLength(255);
        entity.Property(operation => operation.CreatedAt).IsRequired();
        entity.Property(operation => operation.UpdatedAt).IsRequired();
        entity.HasIndex(operation => operation.ProviderSetupIntentId)
            .IsUnique()
            .HasFilter("\"ProviderSetupIntentId\" IS NOT NULL")
            .HasDatabaseName(PaymentDatabaseConstraints.ProviderSetupIntent);
        entity.HasIndex(operation => operation.PaymentCustomerId);
        entity.HasOne<PaymentCustomer>()
            .WithMany()
            .HasForeignKey(operation => operation.PaymentCustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWebhookEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PaymentWebhookEvent>();
        entity.ToTable("payment_webhook_events");
        entity.HasKey(webhookEvent => webhookEvent.Id);
        entity.Property(webhookEvent => webhookEvent.ProviderEventId).HasMaxLength(255).IsRequired();
        entity.Property(webhookEvent => webhookEvent.EventType).HasMaxLength(128).IsRequired();
        entity.Property(webhookEvent => webhookEvent.ProviderSetupIntentId).HasMaxLength(255).IsRequired();
        entity.HasIndex(webhookEvent => webhookEvent.ProviderEventId)
            .IsUnique()
            .HasDatabaseName(PaymentDatabaseConstraints.ProviderWebhookEvent);
        entity.HasIndex(webhookEvent => webhookEvent.ProcessedAt);
        entity.HasIndex(webhookEvent => webhookEvent.ProviderSetupIntentId);
    }
}
