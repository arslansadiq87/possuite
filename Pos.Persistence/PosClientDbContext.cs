// Pos.Persistence/PosClientDbContext.cs
using System.Reflection.Emit;
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
        public DbSet<ItemBarcode> ItemBarcodes => Set<ItemBarcode>();

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


            // ---- Items (no legacy Barcode index) ----
            b.Entity<Item>(e =>
            {
                // Unique SKU but allow many EMPTY SKUs (so new items can start blank)
                // Provider-specific filtered unique index
                string? skuFilter =
                    Database.IsSqlite() ? "length(trim(Sku)) > 0" :
                    Database.IsSqlServer() ? "[Sku] IS NOT NULL AND LTRIM(RTRIM([Sku])) <> ''" :
                    Database.IsNpgsql() ? "\"Sku\" IS NOT NULL AND btrim(\"Sku\") <> ''" :
                    null;

                var skuIdx = e.HasIndex(x => x.Sku).IsUnique();
                if (!string.IsNullOrWhiteSpace(skuFilter))
                    skuIdx.HasFilter(skuFilter);

                // ⛔ Do NOT configure any Barcode index here — the column is removed.
            });


            b.Entity<Product>(e =>
            {
                e.HasIndex(x => x.Name); // optional, for search
            });

            // ---- ItemBarcodes (where barcodes live now) ----
            b.Entity<ItemBarcode>(e =>
            {
                e.Property(x => x.Code)
                    .IsRequired()
                    .HasMaxLength(64)
                    // Uncomment next line if you want case-insensitive uniqueness on SQLite:
                    // .UseCollation("NOCASE")
                    ;

                // Unique barcode strings globally
                e.HasIndex(x => x.Code).IsUnique();

                e.HasOne(x => x.Item)
                 .WithMany(i => i.Barcodes)
                 .HasForeignKey(x => x.ItemId)
                 .OnDelete(DeleteBehavior.Cascade);

                // At most ONE primary barcode per item (filtered unique index)
                string provider = Database.ProviderName ?? string.Empty;
                string? primaryFilter =
                    provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "IsPrimary = 1" :
                    provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ? "[IsPrimary] = 1" :
                    provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "\"IsPrimary\" = TRUE" :
                    null;

                var ixPrimary = e.HasIndex(x => x.ItemId).IsUnique();
                if (!string.IsNullOrWhiteSpace(primaryFilter))
                    ixPrimary.HasFilter(primaryFilter);
            });


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
            // ---------- Purchases ----------
            b.Entity<Purchase>(e =>
            {
                e.HasKey(x => x.Id);

                // Supplier/Party
                e.HasOne<Party>()
                 .WithMany()
                 .HasForeignKey(x => x.PartyId)
                 .OnDelete(DeleteBehavior.Restrict);

                // Target type enums
                e.Property(x => x.Status).HasConversion<int>();
                e.Property(x => x.TargetType).HasConversion<int>();

                // Document fields
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

                // Lines / Payments
                e.HasMany(x => x.Lines)
                 .WithOne()
                 .HasForeignKey(l => l.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);

                e.HasMany(x => x.Payments)
                 .WithOne(p => p.Purchase!)
                 .HasForeignKey(p => p.PurchaseId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Self references (revisions / returns)
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

                // 🔹 NEW: Target Outlet (nullable; used when TargetType == Outlet)
                e.HasOne<Outlet>()
                 .WithMany()
                 .HasForeignKey(x => x.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);

                // 🔹 NEW: Target Warehouse (nullable; used when TargetType == Warehouse)
                e.HasOne<Warehouse>()
                 .WithMany()
                 .HasForeignKey(x => x.WarehouseId)
                 .OnDelete(DeleteBehavior.Restrict);

                // 🔹 NEW: Safety check — exactly one target must be set, matching TargetType
                e.HasCheckConstraint("CK_Purchase_Target",
                    "( [TargetType] = 1 AND [OutletId] IS NOT NULL AND [WarehouseId] IS NULL ) OR " +
                    "( [TargetType] = 2 AND [WarehouseId] IS NOT NULL AND [OutletId] IS NULL )");
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

            // ---------- Warehouses ----------
            b.Entity<Warehouse>(e =>
            {
                e.ToTable("Warehouses");
                e.HasKey(x => x.Id);

                e.Property(x => x.Code).IsRequired().HasMaxLength(16);
                e.Property(x => x.Name).IsRequired().HasMaxLength(128);

                // BaseEntity members already exist in the table model
                e.Property(x => x.PublicId).IsRequired();
                e.Property(x => x.RowVersion).IsRowVersion();
                e.Property(x => x.CreatedAtUtc).IsRequired();

                // Indexes
                e.HasIndex(x => x.Code).IsUnique();
                e.HasIndex(x => x.PublicId).IsUnique();
                e.HasIndex(x => new { x.IsActive, x.Name });

                // Optional metadata lengths
                e.Property(x => x.AddressLine).HasMaxLength(200);
                e.Property(x => x.City).HasMaxLength(100);
                e.Property(x => x.Phone).HasMaxLength(50);
                e.Property(x => x.Note).HasMaxLength(500);
            });



        }
    }
}
