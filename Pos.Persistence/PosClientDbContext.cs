// Pos.Persistence/PosClientDbContext.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;


namespace Pos.Persistence
{
    public class PosClientDbContext : DbContext
    {
        public PosClientDbContext(DbContextOptions<PosClientDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Brand> Brands => Set<Brand>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Item> Items { get; set; }
        public DbSet<Product> Products { get; set; }

        
        public DbSet<Sale> Sales { get; set; }
        public DbSet<SaleLine> SaleLines { get; set; }
        public DbSet<StockEntry> StockEntries { get; set; }
                
        public DbSet<TillSession> TillSessions { get; set; }
        public DbSet<CounterSequence> CounterSequences { get; set; }
                
        public DbSet<Purchase> Purchases { get; set; }
        public DbSet<PurchaseLine> PurchaseLines { get; set; }
        public DbSet<PurchasePayment> PurchasePayments { get; set; }   // payments
        public DbSet<CashLedger> CashLedgers { get; set; }             // cash ledger
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<Outlet> Outlets { get; set; } = null!;
        public DbSet<Counter> Counters { get; set; } = null!;
        public DbSet<UserOutlet> UserOutlets { get; set; } = null!;


        // Use the same connection when options weren't supplied
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlite(DbPath.ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            b.Entity<User>().Property(u => u.Role).HasConversion<int>();

            // ---------- Users ----------
            b.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            // ---------- Sales ----------
            b.Entity<Sale>()
                .HasIndex(s => new { s.CounterId, s.InvoiceNumber, s.Revision })
                .IsUnique();

            b.Entity<Sale>()
                .HasIndex(s => new { s.CounterId, s.InvoiceNumber });

            b.Entity<Sale>().HasIndex(s => s.Status);
            b.Entity<Sale>().HasIndex(s => s.IsReturn);

            b.Entity<Sale>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.CashierId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Sale>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(s => s.SalesmanId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Sale>().Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.TaxTotal).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.Total).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.InvoiceDiscountAmt).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.InvoiceDiscountValue).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.CashAmount).HasColumnType("decimal(18,2)");
            b.Entity<Sale>().Property(x => x.CardAmount).HasColumnType("decimal(18,2)");

            b.Entity<SaleLine>().Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            b.Entity<SaleLine>().Property(x => x.UnitNet).HasColumnType("decimal(18,2)");
            b.Entity<SaleLine>().Property(x => x.LineNet).HasColumnType("decimal(18,2)");
            b.Entity<SaleLine>().Property(x => x.LineTax).HasColumnType("decimal(18,2)");
            b.Entity<SaleLine>().Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

            // ---------- Counter sequences ----------
            b.Entity<CounterSequence>()
                .HasIndex(x => x.CounterId)
                .IsUnique();

            // ---------- Purchases ----------
            b.Entity<Purchase>(e =>
            {
                e.HasKey(x => x.Id);

                e.HasOne<Supplier>()
                 .WithMany()
                 .HasForeignKey(x => x.SupplierId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.Property(x => x.Status).HasConversion<int>();
                e.Property(x => x.TargetType).HasConversion<int>();

                e.Property(x => x.VendorInvoiceNo).HasMaxLength(100);
                e.Property(x => x.DocNo).HasMaxLength(50);

                e.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
                e.Property(x => x.Discount).HasColumnType("decimal(18,2)");
                e.Property(x => x.Tax).HasColumnType("decimal(18,2)");
                e.Property(x => x.OtherCharges).HasColumnType("decimal(18,2)");
                e.Property(x => x.GrandTotal).HasColumnType("decimal(18,2)");
                e.Property(nameof(Purchase.CashPaid)).HasColumnType("decimal(18,2)");
                e.Property(nameof(Purchase.CreditDue)).HasColumnType("decimal(18,2)");

                e.HasIndex(x => x.Status);
                e.HasIndex(x => x.DocNo);

                e.HasMany(x => x.Lines)
                 .WithOne()
                 .HasForeignKey(l => l.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(x => x.Payments)
                 .WithOne(p => p.Purchase!)
                 .HasForeignKey(p => p.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<PurchaseLine>(e =>
            {
                e.HasKey(x => x.Id);

                e.Property(x => x.Qty).HasColumnType("decimal(18,3)");
                e.Property(x => x.UnitCost).HasColumnType("decimal(18,2)");
                e.Property(x => x.Discount).HasColumnType("decimal(18,2)");
                e.Property(x => x.TaxRate).HasColumnType("decimal(5,2)");
                e.Property(x => x.LineTotal).HasColumnType("decimal(18,2)");

                e.HasIndex(x => x.ItemId);

                e.HasOne<Item>()
                 .WithMany()
                 .HasForeignKey(x => x.ItemId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<PurchasePayment>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
                e.Property(x => x.Method).HasConversion<int>(); // enum
                e.Property(x => x.Kind).HasConversion<int>();   // enum
                e.Property(x => x.TsUtc);
                e.HasIndex(x => x.PurchaseId);
                e.HasIndex(x => new { x.OutletId, x.TsUtc });
            });

            // ---------- Suppliers ----------
            b.Entity<Supplier>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                e.Property(x => x.Phone).HasMaxLength(50);
                e.Property(x => x.Email).HasMaxLength(200);
                e.Property(x => x.AddressLine1).HasMaxLength(250);
                e.Property(x => x.AddressLine2).HasMaxLength(250);
                e.Property(x => x.City).HasMaxLength(100);
                e.Property(x => x.State).HasMaxLength(100);
                e.Property(x => x.Country).HasMaxLength(100);
                e.Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)");
                e.HasIndex(x => x.Name);
                e.HasIndex(x => x.IsActive);
            });

            // ---------- Warehouses ----------
            b.Entity<Warehouse>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                e.HasIndex(x => x.Name);
                e.HasIndex(x => x.IsActive);
            });

            // ---------- Cash Ledger ----------
            b.Entity<CashLedger>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Delta).HasColumnType("decimal(18,2)");
                e.Property(x => x.TsUtc);
                e.Property(x => x.RefType).IsRequired();
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
