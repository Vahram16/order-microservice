using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Order.Api.Persistence;

internal sealed class OrderReferenceDataSynchronization
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private OrderReferenceDataSynchronization() { }
    private OrderReferenceDataSynchronization(Guid snapshotId, DateTimeOffset now)
    {
        Id = SingletonId; SnapshotId = snapshotId; CycleStartedAt = now; LastRequestedAt = now; Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid SnapshotId { get; private set; }
    public bool CustomerCompleted { get; private set; }
    public bool ProductCompleted { get; private set; }
    public DateTimeOffset CycleStartedAt { get; private set; }
    public DateTimeOffset LastRequestedAt { get; private set; }
    public DateTimeOffset? CycleCompletedAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public long Version { get; private set; }

    public static OrderReferenceDataSynchronization Start(Guid snapshotId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        return new OrderReferenceDataSynchronization(snapshotId, now);
    }

    public void BeginCycle(Guid snapshotId, DateTimeOffset now)
    {
        if (snapshotId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(snapshotId));
        SnapshotId = snapshotId; CustomerCompleted = false; ProductCompleted = false; CycleStartedAt = now; LastRequestedAt = now; CycleCompletedAt = null; Version++;
    }

    public void MarkRequested(DateTimeOffset now) { LastRequestedAt = now; Version++; }

    public bool MarkCustomerCompleted(Guid snapshotId, DateTimeOffset now)
    {
        if (snapshotId != SnapshotId || CustomerCompleted) return false;
        CustomerCompleted = true; CompleteIfReady(now); Version++; return true;
    }

    public bool MarkProductCompleted(Guid snapshotId, DateTimeOffset now)
    {
        if (snapshotId != SnapshotId || ProductCompleted) return false;
        ProductCompleted = true; CompleteIfReady(now); Version++; return true;
    }

    private void CompleteIfReady(DateTimeOffset now)
    {
        if (!CustomerCompleted || !ProductCompleted) return;
        CycleCompletedAt = now; ReadyAt ??= now;
    }
}

internal sealed class OrderReferenceDataSynchronizationConfiguration : IEntityTypeConfiguration<OrderReferenceDataSynchronization>
{
    public void Configure(EntityTypeBuilder<OrderReferenceDataSynchronization> builder)
    {
        builder.ToTable("order_reference_data_synchronization");
        builder.HasKey(item => item.Id).HasName(OrderDatabaseConstraints.ReferenceDataSynchronizationPrimaryKey);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.SnapshotId).IsRequired();
        builder.Property(item => item.CycleStartedAt).IsRequired();
        builder.Property(item => item.LastRequestedAt).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken().IsRequired();
    }
}
