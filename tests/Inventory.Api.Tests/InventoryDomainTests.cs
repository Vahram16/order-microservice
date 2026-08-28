using Inventory.Api.Domain;

namespace Inventory.Api.Tests;

public sealed class InventoryDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact] public void ReservationPreventsOversell() { var item = InventoryItem.Create(Guid.NewGuid(), 5, Now).Value; Assert.True(item.Reserve(4, Now.AddSeconds(1)).IsSuccess); var result = item.Reserve(2, Now.AddSeconds(2)); Assert.True(result.IsFailure); Assert.Equal(4, item.Reserved); Assert.Equal(1, item.Available); }
    [Fact] public void FailedReserveIsFailureAtomic() { var item = InventoryItem.Create(Guid.NewGuid(), 2, Now).Value; var version = item.Version; var result = item.Reserve(3, Now.AddSeconds(1)); Assert.True(result.IsFailure); Assert.Equal(0, item.Reserved); Assert.Equal(2, item.OnHand); Assert.Equal(version, item.Version); }
    [Fact] public void CommitThenCompensationRestoresStock() { var item = InventoryItem.Create(Guid.NewGuid(), 10, Now).Value; Assert.True(item.Reserve(3, Now.AddSeconds(1)).IsSuccess); Assert.True(item.Commit(3, Now.AddSeconds(2)).IsSuccess); Assert.True(item.RestoreCommitted(3, Now.AddSeconds(3)).IsSuccess); Assert.Equal(10, item.OnHand); }
    [Fact] public void ReservationCommitCompensationIsIdempotent() { var reservation = InventoryReservation.CreateActive(Guid.NewGuid(), [(Guid.NewGuid(), 2)], Now.AddMinutes(5), Now).Value; Assert.True(reservation.Commit(Now.AddSeconds(1)).IsSuccess); Assert.True(reservation.CompensateCommit(Now.AddSeconds(2)).IsSuccess); Assert.True(reservation.CompensateCommit(Now.AddSeconds(3)).IsSuccess); Assert.Equal(InventoryReservationStatus.Released, reservation.Status); }
    [Fact] public void ReservationExpiresOnlyAfterDeadline() { var reservation = InventoryReservation.CreateActive(Guid.NewGuid(), [(Guid.NewGuid(), 1)], Now.AddMinutes(5), Now).Value; Assert.True(reservation.Expire(Now.AddMinutes(4)).IsFailure); Assert.True(reservation.Expire(Now.AddMinutes(5)).IsSuccess); Assert.Equal(InventoryReservationStatus.Expired, reservation.Status); }
    [Fact] public void ExpectedVersionRejectsStaleStockMutation() { var item = InventoryItem.Create(Guid.NewGuid(), 10, Now).Value; Assert.True(item.EnsureExpectedVersion(1).IsSuccess); Assert.True(item.Reserve(1, Now.AddSeconds(1)).IsSuccess); Assert.True(item.EnsureExpectedVersion(1).IsFailure); }
}
