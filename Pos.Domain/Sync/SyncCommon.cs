// Pos.Domain/Sync/SyncCommon.cs
namespace Pos.Domain.Sync;

public enum SyncOp { Upsert = 1, Delete = 2 }

public sealed class SyncEntityRef
{
    public required string Entity { get; init; }      // e.g., "Sale", "Item", "Voucher"
    public required Guid PublicId { get; init; }      // BaseEntity.PublicId
}
