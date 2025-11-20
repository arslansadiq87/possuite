// Pos.Persistence/PosClientDbContext.cs
using System;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Abstractions;
using Pos.Domain.Accounting;
using Pos.Domain.Settings;



namespace Pos.Persistence
{
    public class PosClientDbContext : DbContext
    {
        public PosClientDbContext(DbContextOptions<PosClientDbContext> options) : base(options) { }
        public DbSet<Pos.Persistence.Sync.SyncOutbox> SyncOutbox => Set<Pos.Persistence.Sync.SyncOutbox>();
        public DbSet<Pos.Persistence.Sync.SyncInbox> SyncInbox => Set<Pos.Persistence.Sync.SyncInbox>();
        public DbSet<Pos.Persistence.Sync.SyncCursor> SyncCursors => Set<Pos.Persistence.Sync.SyncCursor>();
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
        
        //public DbSet<InvoiceLocalization> InvoiceLocalizations { get; set; } = default!;
        public DbSet<BarcodeLabelSettings> BarcodeLabelSettings { get; set; } = default!;
        //public DbSet<UserPreference> UserPreferences { get; set; }   // ✅ add this line
        public DbSet<BankAccount> BankAccounts { get; set; } = null!;
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Journal> Journals { get; set; }
        public DbSet<JournalLine> JournalLines { get; set; }
        public DbSet<Pos.Domain.Accounting.GlEntry> GlEntries { get; set; } = null!;
        public DbSet<Pos.Domain.Accounting.Voucher> Vouchers { get; set; } = null!;
        public DbSet<Pos.Domain.Accounting.VoucherLine> VoucherLines { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.Staff> Staff { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.Shift> Shifts { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.ShiftAssignment> ShiftAssignments { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.AttendancePunch> AttendancePunches { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.AttendanceDay> AttendanceDays { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.PayrollRun> PayrollRuns { get; set; } = null!;
        public DbSet<Pos.Domain.Hr.PayrollItem> PayrollItems { get; set; } = null!;
        public DbSet<OtherAccount> OtherAccounts { get; set; } = null!;
        public DbSet<ProductImage> ProductImages => Set<ProductImage>();
        public DbSet<ItemImage> ItemImages => Set<ItemImage>();
        public DbSet<ReceiptTemplate> ReceiptTemplates => Set<ReceiptTemplate>();
        public DbSet<IdentitySettings> IdentitySettings { get; set; } = default!;
        //public DbSet<InvoiceSettingsLocal> InvoiceSettingsLocals => Set<InvoiceSettingsLocal>();
        public DbSet<InvoiceSettingsLocal> InvoiceSettingsLocals { get; set; } = default!;
        // in AppDbContext.cs
        public DbSet<InvoiceSettingsScoped> InvoiceSettingsScoped { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "posclient.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath};Cache=Shared");
            }
        }

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            var provider = Database.ProviderName ?? string.Empty;
            bool isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            bool isSqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
            bool isMySql = provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
            bool isNpgsql = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            //b.ApplyConfiguration(new Configurations.InvoiceSettingsConfig());
            //b.ApplyConfiguration(new Configurations.InvoiceLocalizationConfig());
            b.Entity<User>().Property(u => u.Role).HasConversion<int>();
            b.Entity<User>()
                .HasIndex(u => u.Username)
            .IsUnique();
            b.Entity<Item>(e =>
            // Standalone (ProductId == null) NAME must be unique (case-insensitive)
            {
                var provider = Database.ProviderName ?? string.Empty;
                bool isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                bool isSqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
                bool isMySql = provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
                bool isNpgsql = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

                if (!isSqlite)
                {
                    string colType =
                        isMySql ? "varchar(256)" :
                        isSqlServer ? "nvarchar(256)" :
                        isNpgsql ? "text" :
                                      "TEXT";

                    string normalizedStandaloneSql =
                        isSqlServer ? "CASE WHEN [ProductId] IS NULL THEN LOWER(LTRIM(RTRIM([Name]))) ELSE NULL END"
                      : isNpgsql ? "CASE WHEN \"ProductId\" IS NULL THEN lower(btrim(\"Name\")) ELSE NULL END"
                      : "CASE WHEN `ProductId` IS NULL THEN lower(trim(`Name`)) ELSE NULL END"; // MySQL

                    e.Property<string?>("StandaloneNameNormalized")
                     .HasColumnType(colType)
                     .HasComputedColumnSql(normalizedStandaloneSql, stored: true);

                    e.HasIndex("StandaloneNameNormalized").IsUnique();
                }
                // For SQLite we’ll add a unique expression index via migration (see below).
            });
            b.Entity<Product>(e =>
            {
                // keep your non-unique index for search
                e.HasIndex(x => x.Name);

                var provider = Database.ProviderName ?? string.Empty;
                bool isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                bool isSqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
                bool isMySql = provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
                bool isNpgsql = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);

                if (!isSqlite)
                {
                    string colType =
                        isMySql ? "varchar(256)" :
                        isSqlServer ? "nvarchar(256)" :
                        isNpgsql ? "text" :
                                       "TEXT";

                    string normalizedSql =
                        isSqlServer ? "LOWER(LTRIM(RTRIM([Name])))"
                      : isNpgsql ? "lower(btrim(\"Name\"))"
                      : "lower(trim(`Name`))"; // MySQL as fallback

                    e.Property<string>("NormalizedName")
                     .HasColumnType(colType)
                     .HasComputedColumnSql(normalizedSql, stored: true);

                    e.HasIndex("NormalizedName").IsUnique();
                }
                // For SQLite we’ll add a unique expression index via migration (see below).
            });


            b.Entity<ItemBarcode>(e =>
            {
                e.Property(x => x.Code).IsRequired().HasMaxLength(64);
                e.HasIndex(x => x.Code).IsUnique();

                e.HasOne(x => x.Item)
                 .WithMany(i => i.Barcodes)
                 .HasForeignKey(x => x.ItemId)
                 .OnDelete(DeleteBehavior.Cascade);

                bool isMySqlLocal = provider.Contains("MySql", StringComparison.OrdinalIgnoreCase);
                bool isSqlServerLocal = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
                bool isSqliteLocal = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

                if (isMySqlLocal)
                {
                    // Enforce "at most one primary per item" with a generated column
                    e.Property<int?>("PrimarySlot")
                     .HasColumnType("int")
                     .HasComputedColumnSql("CASE WHEN `IsPrimary` = 1 THEN `ItemId` ELSE NULL END", stored: true);

                    e.HasIndex("PrimarySlot").IsUnique();
                }
                else
                {
                    // Keep your filtered unique index (works on SQL Server, SQLite)
                    string? primaryFilter =
                        isSqlite ? "IsPrimary = 1" :
                        isSqlServer ? "[IsPrimary] = 1" :
                        isNpgsql ? "\"IsPrimary\" = TRUE" :
                        null;

                    var ixPrimary = e.HasIndex(x => x.ItemId).IsUnique();
                    if (!string.IsNullOrWhiteSpace(primaryFilter))
                        ixPrimary.HasFilter(primaryFilter);

                }
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
                e.Property(x => x.LocationType).HasConversion<int>();
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
                 .WithOne(l => l.Purchase)           // <- explicit nav to avoid shadow FK (PurchaseId1)
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
                var targetCheck =
                provider.Contains("MySql", StringComparison.OrdinalIgnoreCase)
                    ? "((`LocationType` = 1 AND `OutletId` IS NOT NULL AND `WarehouseId` IS NULL) OR (`LocationType` = 2 AND `WarehouseId` IS NOT NULL AND `OutletId` IS NULL))"
                : provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase)
                        ? "([LocationType] = 1 AND [OutletId] IS NOT NULL AND [WarehouseId] IS NULL) OR ([LocationType] = 2 AND [WarehouseId] IS NOT NULL AND [OutletId] IS NULL)"
                : provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                        ? "(LocationType = 1 AND OutletId IS NOT NULL AND WarehouseId IS NULL) OR (LocationType = 2 AND WarehouseId IS NOT NULL AND OutletId IS NULL)"
                : provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                        ? "((\"LocationType\" = 1 AND \"OutletId\" IS NOT NULL AND \"WarehouseId\" IS NULL) OR (\"LocationType\" = 2 AND \"WarehouseId\" IS NOT NULL AND \"OutletId\" IS NULL))"
                : null;

                if (!string.IsNullOrWhiteSpace(targetCheck))
                    e.ToTable(t => t.HasCheckConstraint("CK_Purchase_Target", targetCheck!));
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
                var creditLimitCheck =
                    isMySql ? "`CreditLimit` IS NULL OR `CreditLimit` >= 0" :
                    isSqlServer ? "[CreditLimit] IS NULL OR [CreditLimit] >= 0" :
                    isSqlite ? "CreditLimit IS NULL OR CreditLimit >= 0" :
                    isNpgsql ? "\"CreditLimit\" IS NULL OR \"CreditLimit\" >= 0" :
                    null;

                if (!string.IsNullOrWhiteSpace(creditLimitCheck))
                    e.ToTable(t => t.HasCheckConstraint("CK_PartyOutlet_CreditLimit_NonNegative", creditLimitCheck!));
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

            var seCheck =
                isMySql
                    ? "CASE WHEN `RefType` IN ('Opening','TransferOut','TransferIn') THEN `StockDocId` IS NOT NULL ELSE TRUE END"
                : isSqlServer
                    ? "CASE WHEN [RefType] IN ('Opening','TransferOut','TransferIn') THEN [StockDocId] IS NOT NULL ELSE 1 END"
                : isSqlite
                    ? "CASE WHEN RefType IN ('Opening','TransferOut','TransferIn') THEN StockDocId IS NOT NULL ELSE 1 END"
                : isNpgsql
                    ? "CASE WHEN \"RefType\" IN ('Opening','TransferOut','TransferIn') THEN \"StockDocId\" IS NOT NULL ELSE TRUE END"
                : null;

            if (!string.IsNullOrWhiteSpace(seCheck))
                b.Entity<StockEntry>().ToTable(t =>
                    t.HasCheckConstraint("CK_StockEntry_StockDoc_Requirement", seCheck!));

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
            });
            b.Entity<BarcodeLabelSettings>(e =>
            {
                e.ToTable("BarcodeLabelSettings");
                e.HasIndex(x => x.OutletId);
                e.Property(x => x.PrinterName).HasMaxLength(200);
                e.Property(x => x.CodeType).HasMaxLength(20);
            });
            //b.Entity<UserPreference>()
            //.HasIndex(p => p.MachineName)
            //.IsUnique();
            // ---- Accounting (GL) ----
            b.Entity<Account>(e =>
            {
                e.Property(x => x.Code).IsRequired().HasMaxLength(32);
                e.Property(x => x.Name).IsRequired().HasMaxLength(200);
                // ---- relationships ----
                e.HasOne(x => x.Parent)
                 .WithMany()
                 .HasForeignKey(x => x.ParentId)
                 .OnDelete(DeleteBehavior.Restrict);
                // Your current rule: code unique within an outlet (global header has OutletId = null)
                e.HasIndex(x => new { x.OutletId, x.Code }).IsUnique();
                // Fast lookup for system accounts per outlet
                e.HasIndex(x => new { x.SystemKey, x.OutletId })
                 .HasDatabaseName("IX_Account_SystemKey_Outlet");
                // Sibling names unique under the same parent (keeps tree tidy)
                e.HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
            });

            b.Entity<Journal>(e =>
            {
                e.HasIndex(x => x.TsUtc);
                e.Property(x => x.RefType).HasMaxLength(40);
            });

            b.Entity<JournalLine>(e =>
            {
                e.HasIndex(x => x.AccountId);
                e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
            });

            // Party → Account (optional)
            b.Entity<Party>()
             .HasOne(p => p.Account)
             .WithMany()
             .HasForeignKey(p => p.AccountId)
             .OnDelete(DeleteBehavior.SetNull);

            // Voucher + VoucherLine
            b.Entity<Pos.Domain.Accounting.Voucher>(e =>
            {
                e.Property(x => x.RefNo).HasMaxLength(64);
                e.HasMany(v => v.Lines).WithOne(l => l.Voucher)
                 .HasForeignKey(l => l.VoucherId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.Property(x => x.Status).HasDefaultValue(VoucherStatus.Posted);
                e.Property(x => x.RevisionNo).HasDefaultValue(1);
                e.HasIndex(x => x.AmendedFromId);
            });
            b.Entity<Pos.Domain.Accounting.VoucherLine>(e =>
            {
                e.Property(x => x.Debit).HasColumnType("decimal(18,2)");
                e.Property(x => x.Credit).HasColumnType("decimal(18,2)");
            });

            // HR/Payroll
            b.Entity<Pos.Domain.Hr.Staff>(e =>
            {
                e.HasIndex(x => x.Code).IsUnique();
                e.Property(x => x.Code).HasMaxLength(32).IsRequired();
                e.Property(x => x.FullName).HasMaxLength(128).IsRequired();
            });

            b.Entity<Pos.Domain.Hr.Shift>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            });

            b.Entity<Pos.Domain.Hr.ShiftAssignment>(e =>
            {
                e.HasIndex(x => new { x.StaffId, x.FromDateUtc });
                e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<Pos.Domain.Hr.AttendancePunch>(e =>
            {
                e.HasIndex(x => new { x.StaffId, x.TsUtc });
            });

            b.Entity<Pos.Domain.Hr.AttendanceDay>(e =>
            {
                e.HasIndex(x => new { x.StaffId, x.DayUtc }).IsUnique();
            });

            b.Entity<Pos.Domain.Hr.PayrollRun>(e =>
            {
                e.Property(x => x.TotalGross).HasColumnType("decimal(18,2)");
                e.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)");
                e.Property(x => x.TotalNet).HasColumnType("decimal(18,2)");
            });

            b.Entity<Pos.Domain.Hr.PayrollItem>(e =>
            {
                e.Property(x => x.Basic).HasColumnType("decimal(18,2)");
                e.Property(x => x.Allowances).HasColumnType("decimal(18,2)");
                e.Property(x => x.Overtime).HasColumnType("decimal(18,2)");
                e.Property(x => x.Deductions).HasColumnType("decimal(18,2)");
                e.HasOne(x => x.PayrollRun).WithMany().HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Staff).WithMany().HasForeignKey(x => x.StaffId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<ProductImage>(eb =>
            {
                eb.HasIndex(x => new { x.ProductId, x.IsPrimary });
                eb.HasOne(x => x.Product)
                  .WithMany() // keep it simple; we don’t expose a nav prop on Product class now
                  .HasForeignKey(x => x.ProductId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<ItemImage>(eb =>
            {
                eb.HasIndex(x => new { x.ItemId, x.IsPrimary });
                eb.HasOne(x => x.Item)
                  .WithMany()
                  .HasForeignKey(x => x.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<GlEntry>(e =>
            {
                e.HasIndex(x => new { x.DocType, x.DocId });
                e.HasIndex(x => new { x.ChainId, x.IsEffective });
                e.HasIndex(x => new { x.AccountId, x.EffectiveDate });
                e.HasIndex(x => x.PartyId);
                e.Property(x => x.DocSubType).HasConversion<short>();
            });

            b.Entity<ReceiptTemplate>(e =>
            {
                e.ToTable("ReceiptTemplates");
                e.HasKey(x => x.Id);
                // Foreign key (optional – only if you want FK to Outlets)
                e.HasOne(x => x.Outlet)
                 .WithMany()                // or .WithMany(o => o.ReceiptTemplates) if you add a nav
                 .HasForeignKey(x => x.OutletId)
                 .OnDelete(DeleteBehavior.Restrict);
                // Basic property sizing
                e.Property(x => x.LogoAlignment).HasMaxLength(16);
           
                e.HasIndex(x => new { x.DocType, x.OutletId });
            });

            b.Entity<IdentitySettings>(e =>
            {
                e.ToTable("IdentitySettings");
                e.HasKey(x => x.Id);

                e.Property(x => x.OutletDisplayName).HasMaxLength(200);
                e.Property(x => x.AddressLine1).HasMaxLength(200);
                e.Property(x => x.AddressLine2).HasMaxLength(200);
                e.Property(x => x.Phone).HasMaxLength(50);

                e.Property(x => x.BusinessNtn);
                e.Property(x => x.FbrPosId);

                // One row per outlet (or null = GLOBAL)
                e.HasIndex(x => x.OutletId).IsUnique();
            });

            b.Entity<InvoiceSettingsLocal>(e =>
            {
                e.ToTable("InvoiceSettingsLocals");
                e.HasKey(x => x.Id);

                // One settings row per counter
                e.HasIndex(x => x.CounterId).IsUnique();

                // Helpful for "latest settings" queries
                e.HasIndex(x => x.UpdatedAtUtc);

                e.Property(x => x.PrinterName).HasMaxLength(256);
                e.Property(x => x.LabelPrinterName).HasMaxLength(256);
               
            
                // If you want the DB to set UpdatedAtUtc automatically (optional):
                // e.Property(x => x.UpdatedAtUtc)
                //  .HasDefaultValueSql("CURRENT_TIMESTAMP");  // SQLite
            });

            b.Entity<InvoiceSettingsScoped>().ToTable("InvoiceSettingsScoped");




            //var provider = Database.ProviderName ?? string.Empty;
            foreach (var et in b.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(et.ClrType))
                {
                    var eb = b.Entity(et.ClrType);

                    if (isSqlServer)
                    {
                        eb.Property<byte[]>("RowVersion").IsRowVersion();
                    }
                    else if (isMySql)
                    {
                        eb.Property<byte[]>("RowVersion")
                          .IsConcurrencyToken()
                          .ValueGeneratedNever();
                    }
                    else if (isSqlite)
                    {
                        eb.Property<byte[]>("RowVersion")
                          .IsConcurrencyToken()
                          .HasColumnType("BLOB")
                          .ValueGeneratedNever()
                          .HasDefaultValueSql("randomblob(8)"); // ✅ valid SQLite default
                    }
                    else if (isNpgsql)
                    {
                        eb.Property<byte[]>("RowVersion")
                          .IsConcurrencyToken()
                          .ValueGeneratedNever();
                    }
                }
            }
        }
    }
}
