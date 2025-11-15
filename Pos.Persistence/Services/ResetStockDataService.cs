// Pos.Persistence/Services/ResetStockDataService.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Domain.Entities;
using Pos.Domain.Accounting;
using System.Collections.Generic;
using System.Linq;



namespace Pos.Persistence.Services
{
    /// <summary>
    /// Resets all transactional stock/accounting data (sales, purchases, returns, transfers)
    /// without touching Opening Stock or master data. Safe for SQLite (backs up and VACUUMs).
    /// </summary>
    public sealed class ResetStockService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public ResetStockService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task RunAsync(bool wipeMasters = false, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // ---- 0) BACKUP SQLITE FILE (best-effort) ----
            try
            {
                var cn = db.Database.GetDbConnection();
                var dataSource = cn.ConnectionString?.Split(';')
                    .Select(s => s.Trim())
                    .FirstOrDefault(s => s.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("Data Source=".Length);

                if (!string.IsNullOrWhiteSpace(dataSource) && File.Exists(dataSource))
                {
                    var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PosSuite", "backups");
                    Directory.CreateDirectory(dir);
                    var dest = Path.Combine(dir, $"posclient_backup_{stamp}.db");
                    File.Copy(dataSource!, dest, overwrite: false);
                }
            }
            catch { /* ignore backup errors */ }

            db.ChangeTracker.Clear();

            // ---- 1) Gather IDs for targeted deletes (Opening Stock not touched) ----
            var purchaseIds = await db.Purchases.Select(p => p.Id).ToListAsync(ct); // includes returns (IsReturn==true)

            var hasSales = db.Model.FindEntityType(typeof(Sale)) != null;
            var saleIds = hasSales
                ? await db.Sales.Select(s => s.Id).ToListAsync(ct)
                : new List<int>();

            var hasStockDocs = db.Model.FindEntityType(typeof(StockDoc)) != null;
            var transferDocIds = hasStockDocs
                ? await db.StockDocs
                    .Where(d => d.DocType == StockDocType.Transfer)
                    .Select(d => d.Id)
                    .ToListAsync(ct)
                : new List<int>();

            // ---- 2) STOCK ENTRIES FIRST (children) ----
            if (db.Model.FindEntityType(typeof(StockEntry)) != null)
            {
                // Purchases + PurchaseReturns
                if (purchaseIds.Count != 0)
                {
                    await db.StockEntries
                        .Where(se =>
                            (se.RefType == "Purchase" || se.RefType == "PurchaseReturn") &&
                            se.RefId.HasValue && purchaseIds.Contains(se.RefId.Value))
                        .ExecuteDeleteAsync(ct);
                }


                // Sales + SaleReturns
                if (saleIds.Count != 0)
                {
                    await db.StockEntries
                        .Where(se =>
                            (se.RefType == "Sale" || se.RefType == "SaleReturn") &&
                            se.RefId.HasValue && saleIds.Contains(se.RefId.Value))
                        .ExecuteDeleteAsync(ct);
                }


                // Transfers (by StockDocId and optional RefType markers)
                if (transferDocIds.Count != 0)
                {
                    await db.StockEntries
                        .Where(se => se.StockDocId.HasValue && transferDocIds.Contains(se.StockDocId.Value))
                        .ExecuteDeleteAsync(ct);

                    // Harmless if none exist with these labels
                    await db.StockEntries
                        .Where(se => se.RefType == "TransferOut" || se.RefType == "TransferIn")
                        .ExecuteDeleteAsync(ct);
                }
            }

            // ---- 3) PURCHASE CHILDREN (Payments, Lines) ----
            if (purchaseIds.Count != 0)
            {
                if (db.Model.FindEntityType(typeof(PurchasePayment)) != null)
                {
                    await db.PurchasePayments
                        .Where(pp => purchaseIds.Contains(pp.PurchaseId))
                        .ExecuteDeleteAsync(ct);
                }

                await db.PurchaseLines
                    .Where(pl => purchaseIds.Contains(pl.PurchaseId))
                    .ExecuteDeleteAsync(ct);
            }

            // ---- 4) SALES CHILDREN (Lines, optional Payments) ----
            if (saleIds.Count != 0)
            {
                if (db.Model.FindEntityType(typeof(SaleLine)) != null)
                {
                    await db.SaleLines
                        .Where(sl => saleIds.Contains(sl.SaleId))
                        .ExecuteDeleteAsync(ct);
                }

                // If you have a SalePayment entity in the model:
                TryDeleteIfExists(db, "SalePayment", ct);
            }

            // ---- 5) TRANSFER CHILDREN (StockDocLines) ----
            if (transferDocIds.Count != 0 && db.Model.FindEntityType(typeof(StockDocLine)) != null)
            {
                await db.StockDocLines
                    .Where(l => transferDocIds.Contains(l.StockDocId))
                    .ExecuteDeleteAsync(ct);
            }


            // ---- 6) HEADERS (Purchases, Sales, StockDocs) ----

            // A) Break self-referencing FKs on Purchase (RefPurchaseId / RevisedFrom / RevisedTo)
            //    so deletes won't violate Restrict constraints.
            await db.Purchases.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.RefPurchaseId, p => null)
                    .SetProperty(p => p.RevisedFromPurchaseId, p => null)
                    .SetProperty(p => p.RevisedToPurchaseId, p => null),
                ct);

            // B) (Optional but extra safe) delete return headers first
            if (purchaseIds.Count != 0)
            {
                // 1st pass: returns
                await db.Purchases
                    .Where(p => p.IsReturn)
                    .ExecuteDeleteAsync(ct);

                // 2nd pass: remaining (original) purchases
                await db.Purchases
                    .Where(p => !p.IsReturn)
                    .ExecuteDeleteAsync(ct);
            }

            // Sales
            if (saleIds.Count != 0)
            {
                await db.Sales
                    .Where(s => saleIds.Contains(s.Id))
                    .ExecuteDeleteAsync(ct);
            }

            // StockDocs (transfers)
            if (transferDocIds.Count != 0 && hasStockDocs)
            {
                await db.StockDocs
                    .Where(d => transferDocIds.Contains(d.Id))
                    .ExecuteDeleteAsync(ct);
            }


            // ---- 7) ACCOUNTING (GL & ledgers) ----
            if (db.Model.FindEntityType(typeof(GlEntry)) != null)
            {
                await db.GlEntries
                    .Where(g =>
                        g.DocType == GlDocType.Purchase ||
                        g.DocType == GlDocType.PurchaseReturn ||
                        g.DocType == GlDocType.PurchaseRevision ||
                        g.DocType == GlDocType.Sale ||
                        g.DocType == GlDocType.SaleReturn)
                    .ExecuteDeleteAsync(ct);
            }

            if (db.Model.FindEntityType(typeof(PartyLedger)) != null)
            {
                // PartyLedger in your model has no RefType — drop the non-existent filters
                await db.PartyLedgers.ExecuteDeleteAsync(ct);
            }

            if (db.Model.FindEntityType(typeof(PartyBalance)) != null)
            {
                await db.PartyBalances.ExecuteDeleteAsync(ct);
            }

            if (db.Model.FindEntityType(typeof(CashLedger)) != null)
            {
                // If CashLedger has a RefType column in your model, you can narrow this later.
                await db.CashLedgers.ExecuteDeleteAsync(ct);
            }

            // ---- 8) Vouchers (if present) ----
            if (db.Model.FindEntityType(typeof(VoucherLine)) != null)
                await db.VoucherLines.ExecuteDeleteAsync(ct);
            if (db.Model.FindEntityType(typeof(Voucher)) != null)
                await db.Vouchers.ExecuteDeleteAsync(ct);

            // ---- 9) SYNC / OUTBOX (optional; only if present in your model) ----
            TryDeleteIfExists(db, "OutboxMessage", ct);
            TryDeleteIfExists(db, "InboxCursor", ct);
            TryDeleteIfExists(db, "SyncCheckpoint", ct);
            TryDeleteIfExists(db, "SyncState", ct);

            await tx.CommitAsync(ct);

            // ---- 10) VACUUM (SQLite) ----
            try { await db.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken: ct); } catch { /* ignore */ }
        }

        /// <summary>
        /// Delete a DbSet by CLR type name if it exists in the model.
        /// Works around generic Set&lt;T&gt; inference and table-optional modules.
        /// </summary>
        private static void TryDeleteIfExists(DbContext db, string entityClrName, CancellationToken ct)
        {
            var et = db.Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == entityClrName);
            if (et == null) return;

            // db.Set(Type)
            var nonGenericSet = db.GetType()
                                  .GetMethod(nameof(DbContext.Set), new[] { typeof(Type) })?
                                  .Invoke(db, new object[] { et.ClrType });
            if (nonGenericSet is null) return;

            // ExecuteDeleteAsync<T>(IQueryable<T>, CancellationToken)
            var execDel = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.ExecuteDeleteAsync)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 2)
                .MakeGenericMethod(et.ClrType);

            // Invoke (fire-and-forget on this thread)
            execDel.Invoke(null, new object?[] { nonGenericSet, ct });
        }
    }
}
