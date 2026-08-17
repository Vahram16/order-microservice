using Microservices.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Api.Domain;

namespace Payment.Api.Persistence;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options)
    : DbContext(options)
{
    public DbSet<PaymentCustomer> PaymentCustomers => Set<PaymentCustomer>();
    public DbSet<SavedPaymentMethod> PaymentMethods => Set<SavedPaymentMethod>();
    internal DbSet<StripeWebhookInboxEntry> StripeWebhookInbox => Set<StripeWebhookInboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);
        modelBuilder.AddMassTransitOutboxEntities();
    }
}

internal sealed class PaymentCustomerConfiguration : IEntityTypeConfiguration<PaymentCustomer>
{
    public void Configure(EntityTypeBuilder<PaymentCustomer> builder)
    {
        builder.ToTable("payment_customers");
        builder.HasKey(customer => customer.CustomerId);
        builder.Property(customer => customer.CustomerId).ValueGeneratedNever();
        builder.Property(customer => customer.IdentityProvider).HasMaxLength(32).IsRequired();
        builder.Property(customer => customer.IdentitySubject).HasMaxLength(255).IsRequired();
        builder.Property(customer => customer.StripeCustomerId).HasMaxLength(255);
        builder.Property(customer => customer.CreatedAt).IsRequired();
        builder.Property(customer => customer.UpdatedAt).IsRequired();
        builder.Property(customer => customer.Version).IsConcurrencyToken().IsRequired();

        builder.HasIndex(customer => new { customer.IdentityProvider, customer.IdentitySubject })
            .HasDatabaseName("UX_payment_customers_identity")
            .IsUnique();
        builder.HasIndex(customer => customer.StripeCustomerId)
            .HasDatabaseName("UX_payment_customers_stripe_customer_id")
            .HasFilter("\"StripeCustomerId\" IS NOT NULL")
            .IsUnique();
    }
}

internal sealed class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable("payment_methods");
        builder.HasKey(method => method.Id);
        builder.Property(method => method.Id).ValueGeneratedNever();
        builder.Property(method => method.CustomerId).IsRequired();
        builder.Property(method => method.ProviderPaymentMethodId).HasMaxLength(255).IsRequired();
        builder.Property(method => method.Type).HasMaxLength(32).IsRequired();
        builder.Property(method => method.Brand).HasMaxLength(32);
        builder.Property(method => method.Last4).HasMaxLength(4);
        builder.Property(method => method.WalletType).HasMaxLength(32);
        builder.Property(method => method.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(method => method.IsDefault).IsRequired();
        builder.Property(method => method.CreatedAt).IsRequired();
        builder.Property(method => method.UpdatedAt).IsRequired();

        builder.HasIndex(method => method.ProviderPaymentMethodId)
            .HasDatabaseName("UX_payment_methods_provider_id")
            .IsUnique();
        builder.HasIndex(method => new { method.CustomerId, method.IsDefault })
            .HasDatabaseName("UX_payment_methods_default")
            .HasFilter("\"IsDefault\"")
            .IsUnique();

        builder.HasOne<PaymentCustomer>()
            .WithMany()
            .HasForeignKey(method => method.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StripeWebhookInboxConfiguration : IEntityTypeConfiguration<StripeWebhookInboxEntry>
{
    public void Configure(EntityTypeBuilder<StripeWebhookInboxEntry> builder)
    {
        builder.ToTable("stripe_webhook_inbox");
        builder.HasKey(entry => entry.EventId);
        builder.Property(entry => entry.EventId).HasMaxLength(255);
        builder.Property(entry => entry.EventType).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.ObjectId).HasMaxLength(255).IsRequired();
        builder.Property(entry => entry.LastError).HasMaxLength(2000);
        builder.HasIndex(entry => new { entry.ProcessedAtUtc, entry.NextAttemptAtUtc, entry.ReceivedAtUtc });
    }
}
