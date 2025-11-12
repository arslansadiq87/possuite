using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Services.Accounting;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services.Accounting
{
    /// <summary>
    /// EF Core-backed implementation that enqueues sync events and uses IDbContextFactory.
    /// </summary>
    public sealed class PartyPostingService : IPartyPostingService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public PartyPostingService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task PostAsync(
            int partyId,
            BillingScope scope,
            int? outletId,
            PartyLedgerDocType docType,
            int docId,
            decimal debit,
            decimal credit,
            string? memo = null,
            CancellationToken ct = default)
        {
            if (partyId <= 0) throw new ArgumentOutOfRangeException(nameof(partyId));
            if (debit < 0m || credit < 0m) throw new ArgumentOutOfRangeException("Debit/Credit cannot be negative.");
            if (scope == BillingScope.Outlet && (outletId is null || outletId <= 0))
                throw new ArgumentException("Outlet scope requires a valid outletId.", nameof(outletId));

            var ledgerOutletId = scope == BillingScope.Company ? (int?)null : outletId;

            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Create ledger row
            var row = new PartyLedger
            {
                PartyId = partyId,
                OutletId = ledgerOutletId,
                TimestampUtc = DateTime.UtcNow,
                DocType = docType,
                DocId = docId,
                Description = memo,
                Debit = debit,
                Credit = credit
            };

            db.PartyLedgers.Add(row);

            // Upsert/update snapshot
            await UpsertBalanceAsync(db, partyId, ledgerOutletId, debit - credit, ct).ConfigureAwait(false);

            // Persist + enqueue sync (Upsert ledger + balance)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _outbox.EnqueueUpsertAsync(db, row, ct).ConfigureAwait(false);

            // Also replicate the current PartyBalance snapshot
            var bal = await db.PartyBalances
                .FirstAsync(b => b.PartyId == partyId && b.OutletId == ledgerOutletId, ct)
                .ConfigureAwait(false);
            await _outbox.EnqueueUpsertAsync(db, bal, ct).ConfigureAwait(false);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        private static async Task UpsertBalanceAsync(
            PosClientDbContext db,
            int partyId,
            int? outletId,
            decimal delta,
            CancellationToken ct)
        {
            var bal = await db.PartyBalances
                .FirstOrDefaultAsync(b => b.PartyId == partyId && b.OutletId == outletId, ct)
                .ConfigureAwait(false);

            if (bal is null)
            {
                bal = new PartyBalance
                {
                    PartyId = partyId,
                    OutletId = outletId,
                    Balance = 0m,
                    AsOfUtc = DateTime.UtcNow
                };
                db.PartyBalances.Add(bal);
            }

            bal.Balance += delta;
            bal.AsOfUtc = DateTime.UtcNow;
        }
    }
}
