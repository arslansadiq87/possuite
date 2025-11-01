// Pos.Persistence/PosClientDbContext.cs
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Abstractions;


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
        public DbSet<StockDoc> StockDocs { get; set; }
        public DbSet<StockDocLine> StockDocLines { get; set; }
        public DbSet<OpeningStock> OpeningStocks => Set<OpeningStock>();
        public DbSet<OpeningStockLine> OpeningStockLines => Set<OpeningStockLine>();
        public DbSet<OpeningStockDraftLine> OpeningStockDraftLines => Set<OpeningStockDraftLine>();
        public DbSet<InvoiceSettings> InvoiceSettings { get; set; } = default!;
        public DbSet<InvoiceLocalization> InvoiceLocalizations { get; set; } = default!;
        public DbSet<BarcodeLabelSettings> BarcodeLabelSettings { get; set; } = default!;
        public DbSet<UserPreference> UserPreferences { get; set; }   // ✅ add this line

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlite(DbPath.ConnectionString);
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.ApplyConfiguration(new Configurations.InvoiceSettingsConfig());
            b.ApplyConfiguration(new Configurations.InvoiceLocalizationConfig());
            b.Entity<User>().Property(u => u.Role).HasConversion<int>();
            b.Entity<User>()
                .HasIndex(u => u.Username)
            .IsUnique();
            b.Entity<Item>(e =>
            {
                string? skuFilter =
                    Database.IsSqlite() ? "length(trim(Sku)) > 0" :
                    Database.IsSqlServer() ? "[Sku] IS NOT NULL AND LTRIM(RTRIM([Sku])) <> ''" :
                    Database.IsNpgsql() ? "\"Sku\" IS NOT NULL AND btrim(\"Sku\") <> ''" :
                    null;
                var skuIdx = e.HasIndex(x => x.Sku).IsUnique();
                if (!string.IsNullOrWhiteSpace(skuFilter))
                    skuIdx.HasFilter(skuFilter);
            });
            b.Entity<Product>(e =>
            {
                e.HasIndex(x => x.Name); // optional, for search
            });
            b.Entity<ItemBarcode>(e =>
            {
                e.Property(x => x.Code)
                    .IsRequired()
                    .HasMaxLength(64)
                    ;
                e.HasIndex(x => x.Code).IsUnique();
                e.HasOne(x => x.Item)
                 .WithMany(i => i.Barcodes)
                 .HasForeignKey(x => x.ItemId)
                 .OnDelete(DeleteBehavior.Cascade);
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

            b.Entity<Sale>()
                .HasOne(s => s.RefSale)
                .WithMany()
                .HasForeignKey(s => s.RefSaleId)
                .OnDelete(DeleteBehavior.NoAction);
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
            b.Entity<CounterSequence>()
                .HasIndex(x => x.CounterId)
                .IsUnique();
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
                e.HasOne<Outlet>()
                 .WithMany()
                 .HasForeignKey(x => x.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasOne<Warehouse>()
                 .WithMany()
                 .HasForeignKey(x => x.WarehouseId)
                 .OnDelete(DeleteBehavior.Restrict);
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
                e.HasKey(x => x.Id);
                e.HasOne(po => po.Party)
                 .WithMany(p => p.Outlets)
                 .HasForeignKey(po => po.PartyId)
                 .OnDelete(DeleteBehavior.Cascade);            // deleting a Party removes its outlet mappings
                e.HasOne(po => po.Outlet)
                 .WithMany(o => o.PartyOutlets)                // ensure Outlet.PartyOutlets navigation exists
                 .HasForeignKey(po => po.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);           // don’t allow deleting Outlet if mappings exist
                e.HasIndex(po => new { po.PartyId, po.OutletId }).IsUnique();
                e.Property(po => po.CreditLimit).HasPrecision(18, 2); // decimal(18,2)
                e.Property(po => po.AllowCredit).HasDefaultValue(false);
                e.Property(po => po.IsActive).HasDefaultValue(true);
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

            b.Entity<Warehouse>(e =>
            {
                e.ToTable("Warehouses");
                e.HasKey(x => x.Id);
                e.Property(x => x.Code).IsRequired().HasMaxLength(16);
                e.Property(x => x.Name).IsRequired().HasMaxLength(128);
                e.Property(x => x.PublicId).IsRequired();
                //e.Property(x => x.RowVersion).IsRowVersion();
                e.Property(x => x.CreatedAtUtc).IsRequired();
                e.HasIndex(x => x.Code).IsUnique();
                e.HasIndex(x => x.PublicId).IsUnique();
                e.HasIndex(x => new { x.IsActive, x.Name });
                e.Property(x => x.AddressLine).HasMaxLength(200);
                e.Property(x => x.City).HasMaxLength(100);
                e.Property(x => x.Phone).HasMaxLength(50);
                e.Property(x => x.Note).HasMaxLength(500);
            });
            // In OnModelCreating:
            b.Entity<StockEntry>(e =>
            {
                e.Property(x => x.QtyChange).HasColumnType("decimal(18,4)");
                e.Property(x => x.UnitCost).HasColumnType("decimal(18,4)");
                e.Property(x => x.RefType).HasMaxLength(64);
                e.Property(x => x.Note).HasMaxLength(256);

                e.HasOne(x => x.StockDoc)
                 .WithMany(d => d.Lines)
                 .HasForeignKey(x => x.StockDocId)
                 .OnDelete(DeleteBehavior.Restrict)
                 .IsRequired(false);

                // ✅ add these
                e.HasIndex(x => new { x.ItemId, x.LocationType, x.LocationId })
                 .HasDatabaseName("IX_StockEntries_Item_Loc");

                e.HasIndex(x => x.Ts)
                 .HasDatabaseName("IX_StockEntries_Ts");

                e.HasIndex(x => new { x.RefType, x.RefId })
                 .HasDatabaseName("IX_StockEntries_Ref");
            });

            // Opening Stock configuration
            // Opening Stock header
            b.Entity<OpeningStock>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Status).HasConversion<int>();
                b.Property(x => x.TsUtc).HasColumnType("datetime");

                // Posted lines (child collection)
                b.HasMany(x => x.Lines)
                 .WithOne(x => x.OpeningStock)
                 .HasForeignKey(x => x.OpeningStockId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            // Posted lines config
            b.Entity<OpeningStockLine>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Qty).HasPrecision(18, 4);
                b.Property(x => x.UnitCost).HasPrecision(18, 4);
                b.HasIndex(x => x.OpeningStockId);
            });

            // Draft lines (StockDoc-centric; no relationship to OpeningStock)
            b.Entity<OpeningStockDraftLine>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Qty).HasPrecision(18, 4);
                b.Property(x => x.UnitCost).HasPrecision(18, 4);
                b.HasIndex(x => x.StockDocId);
            });


            b.Entity<StockEntry>().HasCheckConstraint(
                "CK_StockEntry_StockDoc_Requirement",
                // When RefType is Opening/TransferOut/TransferIn => StockDocId MUST be NOT NULL
                // Otherwise (Sale, Purchase, etc.) it MAY be NULL
                "CASE " +
                " WHEN [RefType] IN ('Opening','TransferOut','TransferIn') THEN [StockDocId] IS NOT NULL " +
                " ELSE 1 " +
                "END"
            );


            // optional: enum as int
            b.Entity<StockDoc>()
             .Property(x => x.DocType).HasConversion<int>();
            b.Entity<StockDoc>()
             .Property(x => x.Status).HasConversion<int>();
            b.Entity<StockEntry>()
             .Property(x => x.LocationType).HasConversion<int>();

            // --- StockDocLine ---
            b.Entity<StockDocLine>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.QtyExpected).HasColumnType("decimal(18,4)");
                e.Property(x => x.QtyReceived).HasColumnType("decimal(18,4)");
                e.Property(x => x.UnitCostExpected).HasColumnType("decimal(18,4)");

                e.Property(x => x.SkuSnapshot).HasMaxLength(64);
                e.Property(x => x.ItemNameSnapshot).HasMaxLength(256);
                e.Property(x => x.Remarks).HasMaxLength(256);
                e.Property(x => x.VarianceNote).HasMaxLength(256);

                e.HasIndex(x => x.StockDocId);
                e.HasIndex(x => new { x.ItemId, x.StockDocId });
            });
            // --- StockDoc (transfer extras) ---
            b.Entity<StockDoc>(e =>
            {
                // TransferNo unique when present
                e.HasIndex(x => x.TransferNo).IsUnique();

                // relationship: StockDoc -> TransferLines (StockDocLine)
                e.HasMany(d => d.TransferLines)
                 .WithOne()
                 .HasForeignKey(l => l.StockDocId)
                 .OnDelete(DeleteBehavior.Cascade);

                // NOTE: We keep your existing StockDoc -> Lines (StockEntry) relationship
                // for Opening and other flows (it’s already implicit via StockEntry.StockDocId).
            });
            b.Entity<BarcodeLabelSettings>(e =>
            {
                e.ToTable("BarcodeLabelSettings");
                e.HasIndex(x => x.OutletId);
                e.Property(x => x.PrinterName).HasMaxLength(200);
                e.Property(x => x.CodeType).HasMaxLength(20);
            });

            b.Entity<UserPreference>()
            .HasIndex(p => p.MachineName)
            .IsUnique();

            // ---- Provider-aware RowVersion mapping for ALL BaseEntity types ----
            var provider = Database.ProviderName ?? string.Empty;
            foreach (var et in b.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(et.ClrType))
                {
                    var eb = b.Entity(et.ClrType);

                    if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        // SQL Server can use real rowversion/timestamp
                        eb.Property<byte[]>("RowVersion").IsRowVersion();
                    }
                    else
                    {
                        // SQLite: treat RowVersion as a plain concurrency token
                        // and ensure a non-null default at the DB level.
                        eb.Property<byte[]>("RowVersion")
                          .IsConcurrencyToken()
                          .ValueGeneratedNever()
                          .HasDefaultValueSql("X''"); // empty BLOB default
                    }
                }
            }

        }
    }
}
