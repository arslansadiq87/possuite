using Microsoft.EntityFrameworkCore;

namespace Pos.Server.Api;

public sealed class ServerDbContext : DbContext
{
    public ServerDbContext(DbContextOptions<ServerDbContext> options) : base(options) { }

    public DbSet<ServerChange> Changes => Set<ServerChange>();
    public DbSet<ServerCursor> Cursors => Set<ServerCursor>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Append-only global change feed
        b.Entity<ServerChange>(e =>
        {
            e.ToTable("ServerChanges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.Property(x => x.Token).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();

            e.Property(x => x.Entity).IsRequired().HasMaxLength(128);
            e.Property(x => x.PublicId).IsRequired();
            e.Property(x => x.Op).IsRequired(); // maps to Pos.Domain.Sync.SyncOp (int)
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.TsUtc).IsRequired();
            e.Property(x => x.SourceTerminal).IsRequired().HasMaxLength(128);
        });

        // Per-terminal watermarks
        b.Entity<ServerCursor>(e =>
        {
            e.ToTable("ServerCursors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();

            e.Property(x => x.TerminalId).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.TerminalId).IsUnique();
            e.Property(x => x.LastToken).IsRequired();
        });
    }
}

public sealed class ServerChange
{
    public long Id { get; set; }
    public long Token { get; set; }
    public string Entity { get; set; } = default!;
    public Guid PublicId { get; set; }
    public int Op { get; set; }
    public string PayloadJson { get; set; } = default!;
    public DateTime TsUtc { get; set; }
    public string SourceTerminal { get; set; } = default!;
}

public sealed class ServerCursor
{
    public int Id { get; set; }
    public string TerminalId { get; set; } = default!;
    public long LastToken { get; set; }
}
