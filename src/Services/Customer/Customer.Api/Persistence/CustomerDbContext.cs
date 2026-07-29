using Customer.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customer.Api.Persistence;

public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options)
    : DbContext(options)
{
    public DbSet<Domain.Customer> Customers => Set<Domain.Customer>();
    internal DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    internal DbSet<CustomerAuditEntry> CustomerAuditEntries => Set<CustomerAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CustomerDbContext).Assembly);
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Domain.Customer>
{
    public void Configure(EntityTypeBuilder<Domain.Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.IdentityProvider).HasMaxLength(32).IsRequired();
        builder.Property(customer => customer.IdentitySubject).HasMaxLength(255).IsRequired();
        builder.Property(customer => customer.FirstName).HasMaxLength(100);
        builder.Property(customer => customer.LastName).HasMaxLength(100);
        builder.Property(customer => customer.Email).HasMaxLength(320);
        builder.Property(customer => customer.PhoneNumber).HasMaxLength(32);
        builder.Property(customer => customer.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();
        builder.Property(customer => customer.CreatedAt).IsRequired();
        builder.Property(customer => customer.UpdatedAt).IsRequired();
        builder.Property(customer => customer.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(customer => new
        {
            customer.IdentityProvider,
            customer.IdentitySubject
        })
            .HasDatabaseName(CustomerDatabaseConstraints.Identity)
            .IsUnique();

        builder.HasMany(customer => customer.Addresses)
            .WithOne()
            .HasForeignKey(address => address.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(customer => customer.Addresses)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses");
        builder.HasKey(address => address.Id)
            .HasName(CustomerDatabaseConstraints.AddressPrimaryKey);
        builder.Property(address => address.Id).ValueGeneratedNever();
        builder.Property(address => address.CustomerId).IsRequired();
        builder.Property(address => address.Label).HasMaxLength(50);
        builder.Property(address => address.RecipientName).HasMaxLength(200).IsRequired();
        builder.Property(address => address.Line1).HasMaxLength(200).IsRequired();
        builder.Property(address => address.Line2).HasMaxLength(200);
        builder.Property(address => address.City).HasMaxLength(100).IsRequired();
        builder.Property(address => address.Region).HasMaxLength(100);
        builder.Property(address => address.PostalCode).HasMaxLength(32).IsRequired();
        builder.Property(address => address.CountryCode)
            .HasConversion(code => code.Value, value => CountryCode.FromPersistence(value))
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();
        builder.Property(address => address.PhoneNumber).HasMaxLength(32);
        builder.Property(address => address.IsDefaultShipping).IsRequired();
        builder.Property(address => address.IsDefaultBilling).IsRequired();
        builder.Property(address => address.CreatedAt).IsRequired();
        builder.Property(address => address.UpdatedAt).IsRequired();

        builder.HasIndex(address => address.CustomerId);
        builder.HasIndex(address => new { address.CustomerId, address.IsDefaultShipping })
            .HasDatabaseName(CustomerDatabaseConstraints.DefaultShipping)
            .HasFilter("\"IsDefaultShipping\"")
            .IsUnique();
        builder.HasIndex(address => new { address.CustomerId, address.IsDefaultBilling })
            .HasDatabaseName(CustomerDatabaseConstraints.DefaultBilling)
            .HasFilter("\"IsDefaultBilling\"")
            .IsUnique();
    }
}

internal sealed class CustomerAuditEntryConfiguration : IEntityTypeConfiguration<CustomerAuditEntry>
{
    public void Configure(EntityTypeBuilder<CustomerAuditEntry> builder)
    {
        builder.ToTable("customer_audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.CustomerId).IsRequired();
        builder.Property(entry => entry.ActorSubject).HasMaxLength(255).IsRequired();
        builder.Property(entry => entry.Action).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.OccurredAt).IsRequired();
        builder.Property(entry => entry.CustomerVersion).IsRequired();
        builder.HasIndex(entry => new { entry.CustomerId, entry.OccurredAt });
        builder.HasOne<Domain.Customer>()
            .WithMany()
            .HasForeignKey(entry => entry.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
