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
        //public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<Outlet> Outlets { get; set; } = null!;
        public DbSet<Counter> Counters { get; set; } = null!;
        public DbSet<UserOutlet> UserOutlets { get; set; } = null!;
        public DbSet<CounterBinding> CounterBindings { get; set; } = null!;
        public DbSet<Party> Parties { get; set; } = null!;
        public DbSet<PartyRole> PartyRoles { get; set; } = null!;
        public DbSet<PartyOutlet> PartyOutlets { get; set; } = null!;
        public DbSet<PartyLedger> PartyLedgers { get; set; } = null!;
        public DbSet<PartyBalance> PartyBalances { get; set; } = null!;
        public DbSet<SupplierCredit> SupplierCredits => Set<SupplierCredit>();

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

                e.HasOne<Party>()
                 .WithMany()
                 .HasForeignKey(x => x.PartyId)
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
                e.HasOne(x => x.RefPurchase)
                 .WithMany()
                 .HasForeignKey(x => x.RefPurchaseId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne<Purchase>()
                 .WithMany()
                 .HasForeignKey(x => x.RevisedFromPurchaseId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne<Purchase>()
                 .WithMany()
                 .HasForeignKey(x => x.RevisedToPurchaseId)
                 .OnDelete(DeleteBehavior.Restrict);
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
                e.HasOne<PurchaseLine>()
                 .WithMany()
                 .HasForeignKey(x => x.RefPurchaseLineId)
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

            b.Entity<Brand>().HasIndex(x => x.Name).IsUnique();
            b.Entity<Category>().HasIndex(x => x.Name).IsUnique();

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

            b.Entity<CounterBinding>(e =>
            {
                e.ToTable("CounterBindings");
                e.HasIndex(x => x.MachineId).IsUnique();
                e.HasIndex(x => x.CounterId).IsUnique(); // <— ensures "same counter not assigned to multiple PCs"

                e.Property(x => x.MachineId).HasMaxLength(128).IsRequired();
                e.Property(x => x.MachineName).HasMaxLength(128);

                e.HasOne(x => x.Outlet)
                    .WithMany(o => o.CounterBindings)
                    .HasForeignKey(x => x.OutletId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.Counter)
                    .WithMany(c => c.CounterBindings)
                    .HasForeignKey(x => x.CounterId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<Party>(e =>
            {
                e.Property(p => p.Name).HasMaxLength(200).IsRequired();
                e.Property(p => p.Email).HasMaxLength(200);
                e.Property(p => p.Phone).HasMaxLength(50);
                e.Property(p => p.TaxNumber).HasMaxLength(50);
                e.HasIndex(p => new { p.IsActive, p.Name });
            });

            b.Entity<PartyRole>(e =>
            {
                e.HasOne(r => r.Party)
                 .WithMany(p => p.Roles)
                 .HasForeignKey(r => r.PartyId)
                 .OnDelete(DeleteBehavior.Cascade); // roles can cascade
                e.HasIndex(r => new { r.PartyId, r.Role }).IsUnique();
            });

            b.Entity<PartyOutlet>(e =>
            {
                e.ToTable("PartyOutlets");

                // Key comes from BaseEntity.Id; no need to redefine unless you changed it
                e.HasKey(x => x.Id);

                // Relationships
                e.HasOne(po => po.Party)
                 .WithMany(p => p.Outlets)
                 .HasForeignKey(po => po.PartyId)
                 .OnDelete(DeleteBehavior.Cascade);            // deleting a Party removes its outlet mappings

                e.HasOne(po => po.Outlet)
                 .WithMany(o => o.PartyOutlets)                // ensure Outlet.PartyOutlets navigation exists
                 .HasForeignKey(po => po.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);           // don’t allow deleting Outlet if mappings exist

                // Uniqueness: one Party can only be mapped once to a given Outlet
                e.HasIndex(po => new { po.PartyId, po.OutletId }).IsUnique();

                // Money/flags with defaults
                e.Property(po => po.CreditLimit).HasPrecision(18, 2); // decimal(18,2)
                e.Property(po => po.AllowCredit).HasDefaultValue(false);
                e.Property(po => po.IsActive).HasDefaultValue(true);

                // Optional safety: don’t allow negative credit limits
                e.HasCheckConstraint("CK_PartyOutlet_CreditLimit_NonNegative", "[CreditLimit] IS NULL OR [CreditLimit] >= 0");
            });


            b.Entity<PartyLedger>(e =>
            {
                e.HasIndex(pl => new { pl.PartyId, pl.OutletId, pl.TimestampUtc });
                e.Property(pl => pl.Debit).HasPrecision(18, 2);
                e.Property(pl => pl.Credit).HasPrecision(18, 2);
                e.Property(pl => pl.Description).HasMaxLength(500);

                e.HasOne(pl => pl.Party)
                 .WithMany()
                 .HasForeignKey(pl => pl.PartyId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(pl => pl.Outlet)
                 .WithMany()
                 .HasForeignKey(pl => pl.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<PartyBalance>(e =>
            {
                e.HasIndex(pb => new { pb.PartyId, pb.OutletId }).IsUnique();
                e.Property(pb => pb.Balance).HasPrecision(18, 2);
            });

            b.Entity<SupplierCredit>(e =>
            {
                e.Property(x => x.Amount).HasPrecision(18, 2);
            });


        }
    }
}
