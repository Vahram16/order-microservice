using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Product.Api.Persistence;

public sealed class ProductDbContext(DbContextOptions<ProductDbContext> options)
    : DbContext(options)
{
    public DbSet<Domain.Product> Products => Set<Domain.Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Domain.Product>
{
    public void Configure(EntityTypeBuilder<Domain.Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();
        builder.Property(product => product.Sku).HasMaxLength(Domain.Product.MaximumSkuLength).IsRequired();
        builder.Property(product => product.Name).HasMaxLength(Domain.Product.MaximumNameLength).IsRequired();
        builder.Property(product => product.Description).HasMaxLength(Domain.Product.MaximumDescriptionLength);
        builder.Property(product => product.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(product => product.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(product => product.CreatedAt).IsRequired();
        builder.Property(product => product.UpdatedAt).IsRequired();
        builder.Property(product => product.Version).IsConcurrencyToken().IsRequired();

        builder.HasIndex(product => product.Sku)
            .HasDatabaseName(ProductDatabaseConstraints.Sku)
            .IsUnique();
    }
}
