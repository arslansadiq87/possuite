// Pos.Persistence/AppDbContext.cs
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Abstractions;
using Pos.Domain.Entities;
using CounterEntity = Pos.Domain.Entities.Counter; // avoid generic Counter<T> clash


namespace Pos.Persistence // ← keep a namespace (recommended)
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // --- DbSets used by API & seeding ---
        public DbSet<User> Users => Set<User>();
        public DbSet<Outlet> Outlets => Set<Outlet>();
        public DbSet<CounterEntity> Counters => Set<CounterEntity>();
        public DbSet<UserOutlet> UserOutlets => Set<UserOutlet>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Item> Items => Set<Item>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleLine> SaleLines => Set<SaleLine>();
        public DbSet<TillSession> TillSessions => Set<TillSession>();
        // ===== Purchasing =====
        public DbSet<Purchase> Purchases { get; set; }
        public DbSet<PurchaseLine> PurchaseLines { get; set; }
        public DbSet<PurchasePayment> PurchasePayments => Set<PurchasePayment>();
        public DbSet<CashLedger> CashLedgers => Set<CashLedger>();
        
        
                


        protected override void OnModelCreating(ModelBuilder b)
        {
            // Apply defaults to all BaseEntity children (your original code)
            foreach (var e in b.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(e.ClrType))
                {
                    b.Entity(e.ClrType).Property("PublicId")
                        .HasDefaultValueSql("NEWID()")     // SQL Server GUID
                        .IsRequired();

                    b.Entity(e.ClrType).Property("CreatedAtUtc")
                        .HasDefaultValueSql("GETUTCDATE()"); // server-side UTC

                    b.Entity(e.ClrType).Property("RowVersion")
                        .IsRowVersion(); // optimistic concurrency
                }
            }

            // Useful relationship: Sale (return) → RefSale (original)
            b.Entity<Sale>()
                .HasOne(s => s.RefSale)
                .WithMany()
                .HasForeignKey(s => s.RefSaleId)
                .OnDelete(DeleteBehavior.NoAction);

            base.OnModelCreating(b);

            // ---------- Purchases ----------
            b.Entity<Purchase>(e =>
            {
                e.HasKey(x => x.Id);

                // Strings
                e.Property(x => x.VendorInvoiceNo).HasMaxLength(100);
                e.Property(x => x.DocNo).HasMaxLength(50);

                // Money (match your style)
                e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
                e.Property(x => x.Discount).HasColumnType("decimal(18,2)");
                e.Property(x => x.Tax).HasColumnType("decimal(18,2)");
                e.Property(x => x.OtherCharges).HasColumnType("decimal(18,2)");
                e.Property(x => x.GrandTotal).HasColumnType("decimal(18,2)");

                // Indexes for lookups
                e.HasIndex(x => x.Status);
                e.HasIndex(x => x.DocNo);

                // Lines (cascade delete lines when a purchase is deleted)
                e.HasMany(x => x.Lines)
                 .WithOne()
                 .HasForeignKey(l => l.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<PurchaseLine>(e =>
            {
                e.HasKey(x => x.Id);

                // Numbers
                e.Property(x => x.Qty).HasColumnType("decimal(18,3)");
                e.Property(x => x.UnitCost).HasColumnType("decimal(18,2)");
                e.Property(x => x.Discount).HasColumnType("decimal(18,2)");
                e.Property(x => x.TaxRate).HasColumnType("decimal(5,2)");
                e.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

                // Fast item lookups later
                e.HasIndex(x => x.ItemId);
            });

            b.Entity<PurchasePayment>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                e.HasIndex(x => x.PurchaseId);
                e.HasIndex(x => new { x.OutletId, x.TsUtc });
            });

            b.Entity<CashLedger>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Delta).HasColumnType("decimal(18,2)");
                e.HasIndex(x => new { x.OutletId, x.TsUtc });
            });

            b.Entity<UserOutlet>(cfg =>
            {
                cfg.HasKey(x => new { x.UserId, x.OutletId });

                cfg.HasOne(x => x.User)
                   .WithMany(u => u.UserOutlets)
                   .HasForeignKey(x => x.UserId)
                   .OnDelete(DeleteBehavior.Cascade);

                cfg.HasOne(x => x.Outlet)
                   .WithMany(o => o.UserOutlets)
                   .HasForeignKey(x => x.OutletId)
                   .OnDelete(DeleteBehavior.Cascade);
            });

        }
    }
}
