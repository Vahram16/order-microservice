using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Order.Api.Persistence;

internal sealed class OrderReferenceDataSynchronization
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private OrderReferenceDataSynchronization()
    {
    }

    private OrderReferenceDataSynchronization(Guid snapshotId, DateTimeOffset now)
    {
        Id = SingletonId;
        SnapshotId = snapshotId;
        CycleStartedAt = now;
        CustomerLastRequestedAt = now;
        ProductLastRequestedAt = now;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid? CustomerAfterCustomerId { get; private set; }
    public Guid? ProductAfterProductId { get; private set; }
    public bool CustomerCompleted { get; private set; }
    public bool ProductCompleted { get; private set; }
    public DateTimeOffset CycleStartedAt { get; private set; }
    public DateTimeOffset CustomerLastRequestedAt { get; private set; }
    public DateTimeOffset ProductLastRequestedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public long Version { get; private set; }

    public static OrderReferenceDataSynchronization Start(Guid snapshotId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        return new OrderReferenceDataSynchronization(snapshotId, now);
    }

    public void BeginCycle(Guid snapshotId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);

        SnapshotId = snapshotId;
        CustomerAfterCustomerId = null;
        ProductAfterProductId = null;
        CustomerCompleted = false;
        ProductCompleted = false;
        CycleStartedAt = now;
        CustomerLastRequestedAt = now;
        ProductLastRequestedAt = now;
        Version++;
    }

    public void MarkCustomerRequested(DateTimeOffset now)
    {
        CustomerLastRequestedAt = LaterOf(CustomerLastRequestedAt, now);
        Version++;
    }

    public void MarkProductRequested(DateTimeOffset now)
    {
        ProductLastRequestedAt = LaterOf(ProductLastRequestedAt, now);
        Version++;
    }

    public bool ApplyCustomerPage(
        Guid snapshotId,
        Guid? afterCustomerId,
        Guid? nextAfterCustomerId,
        bool isLastPage,
        DateTimeOffset now)
    {
        if (snapshotId != SnapshotId ||
            CustomerCompleted ||
            afterCustomerId != CustomerAfterCustomerId)
        {
            return false;
        }

        CustomerAfterCustomerId = nextAfterCustomerId;
        CustomerCompleted = isLastPage;
        CompleteIfReady(now);
        Version++;
        return true;
    }

    public bool ApplyProductPage(
        Guid snapshotId,
        Guid? afterProductId,
        Guid? nextAfterProductId,
        bool isLastPage,
        DateTimeOffset now)
    {
        if (snapshotId != SnapshotId ||
            ProductCompleted ||
            afterProductId != ProductAfterProductId)
        {
            return false;
        }

        ProductAfterProductId = nextAfterProductId;
        ProductCompleted = isLastPage;
        CompleteIfReady(now);
        Version++;
        return true;
    }

    private void CompleteIfReady(DateTimeOffset now)
    {
        if (!CustomerCompleted || !ProductCompleted)
        {
            return;
        }

        LastCompletedAt = now;
        ReadyAt ??= now;
    }

    private static DateTimeOffset LaterOf(DateTimeOffset current, DateTimeOffset candidate) =>
        candidate > current ? candidate : current;
}

internal sealed class OrderReferenceDataSynchronizationConfiguration
    : IEntityTypeConfiguration<OrderReferenceDataSynchronization>
{
    public void Configure(EntityTypeBuilder<OrderReferenceDataSynchronization> builder)
    {
        builder.ToTable("order_reference_data_synchronization");
        builder.HasKey(item => item.Id)
            .HasName(OrderDatabaseConstraints.ReferenceDataSynchronizationPrimaryKey);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.SnapshotId).IsRequired();
        builder.Property(item => item.CycleStartedAt).IsRequired();
        builder.Property(item => item.CustomerLastRequestedAt).IsRequired();
        builder.Property(item => item.ProductLastRequestedAt).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
    }
}