// Pos.Domain/Sync/SyncContracts.cs
namespace Pos.Domain.Sync;

public sealed class SyncEnvelope
{
    public required string Entity { get; init; }      // type name
    public required SyncOp Op { get; init; }
    public required Guid PublicId { get; init; }
    public required string PayloadJson { get; init; } // normalized JSON view of the entity
    public required DateTime TsUtc { get; init; }     // logical change time
    public byte[]? RowVersion { get; init; }          // optional concurrency proof
}

public sealed class SyncBatch
{
    public required string TerminalId { get; init; }  // your counter identity
    public required long FromToken { get; init; }     // last acknowledged token
    public required List<SyncEnvelope> Changes { get; init; } = new();
}
