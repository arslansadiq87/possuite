// Pos.Persistence/Sync/ClientSyncEntities.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Pos.Persistence.Sync;

[Index(nameof(Token), IsUnique = true)]
public class SyncOutbox
{
    public long Id { get; set; }
    [MaxLength(64)] public string Entity { get; set; } = default!;
    public Guid PublicId { get; set; }
    public int Op { get; set; } // SyncOp enum int
    public string PayloadJson { get; set; } = default!;
    public DateTime TsUtc { get; set; }
    public long Token { get; set; } // local monotonic sequence (ordering)
}

[Index(nameof(Token), IsUnique = true)]
public class SyncInbox
{
    public long Id { get; set; }
    [MaxLength(64)] public string Entity { get; set; } = default!;
    public Guid PublicId { get; set; }
    public int Op { get; set; }
    public string PayloadJson { get; set; } = default!;
    public DateTime TsUtc { get; set; }
    public long Token { get; set; } // server token applied
}

public class SyncCursor
{
    public int Id { get; set; }
    [MaxLength(64)] public string Name { get; set; } = "server"; // only one row for now
    public long LastToken { get; set; }
}
