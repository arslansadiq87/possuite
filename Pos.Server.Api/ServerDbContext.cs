using Microsoft.EntityFrameworkCore;

namespace Pos.Server.Api;

public class ServerDbContext : DbContext
{
    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }

    public DbSet<ServerChange> Changes => Set<ServerChange>(); // append-only feed
    public DbSet<ServerCursor> Cursors => Set<ServerCursor>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ServerChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.Entity).HasMaxLength(64);
            e.Property(x => x.SourceTerminal).HasMaxLength(64);
        });
        b.Entity<ServerCursor>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TerminalId).HasMaxLength(64);
            e.HasIndex(x => x.TerminalId).IsUnique();
        });
    }
}

public class ServerChange
{
    public long Id { get; set; }
    public long Token { get; set; }               // server-global token
    public string Entity { get; set; } = default!;
    public Guid PublicId { get; set; }
    public int Op { get; set; }                   // SyncOp
    public string PayloadJson { get; set; } = default!;
    public DateTime TsUtc { get; set; }
    public string SourceTerminal { get; set; } = default!;
}

public class ServerCursor
{
    public int Id { get; set; }
    public string TerminalId { get; set; } = default!;
    public long LastToken { get; set; }
}
