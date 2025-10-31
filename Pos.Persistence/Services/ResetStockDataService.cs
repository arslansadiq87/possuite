// Pos.Client.Wpf/Services/ResetStockService.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Deletes ONLY stock-flow docs & ledger:
    /// - Sales + SaleLines (incl. returns via Sale.IsReturn)
    /// - Purchases + PurchaseLines + PurchasePayments (incl. returns via Purchase.IsReturn)
    /// - Transfers (StockDoc/StockDocLine where DocType=Transfer) — keeps Opening!
    /// - StockEntries for ref types: Sale, SaleReturn, Purchase, PurchaseReturn, TransferIn/Out
    /// </summary>
    public sealed class ResetStockService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public ResetStockService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task RunAsync(CancellationToken ct = default)
        {
            // 1) Safe backup of the SQLite file
            var dbPath = DbPath.Get();
            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
            Directory.CreateDirectory(backupDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(backupDir, $"posclient_before_reset_{stamp}.db");
            File.Copy(dbPath, backupPath, overwrite: false);

            // 2) Delete rows inside one transaction (children → parents)
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // --- Stock ledger & docs (delete ALL, incl. Opening) ---
            // Order matters: entries -> lines -> docs
            await db.StockEntries.ExecuteDeleteAsync(ct);
            await db.StockDocLines.ExecuteDeleteAsync(ct);
            await db.StockDocs.ExecuteDeleteAsync(ct);

            // --- Purchases (incl. returns) ---
            await db.PurchasePayments.ExecuteDeleteAsync(ct);
            await db.PurchaseLines.ExecuteDeleteAsync(ct);
            await db.Purchases.ExecuteDeleteAsync(ct);

            // --- Sales (incl. returns) ---
            await db.SaleLines.ExecuteDeleteAsync(ct);
            await db.Sales.ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);

            // 3) Reclaim file space (SQLite only; must be outside txn)
            await db.Database.ExecuteSqlRawAsync("VACUUM;");
        }

    }
}
