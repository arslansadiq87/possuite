using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Client.Wpf.Services
{
    public enum BillingScope { Company, Outlet }

    public sealed class PartyPostingService
    {
        private readonly PosClientDbContext _db;
        private readonly IOutboxWriter _outbox; // ⬅️ add

        public PartyPostingService(PosClientDbContext db, IOutboxWriter outbox) // ⬅️ change
        {
            _db = db;
            _outbox = outbox; // ⬅️ add
        }

        public async Task PostAsync(int partyId, BillingScope scope, int? outletId,
            PartyLedgerDocType docType, int docId, decimal debit, decimal credit, string? memo = null)
        {
            int? ledgerOutletId = scope == BillingScope.Company ? null : outletId;

            var row = new PartyLedger
            {
                PartyId = partyId,
                OutletId = ledgerOutletId,
                TimestampUtc = DateTime.UtcNow,   // match entity
                DocType = docType,
                DocId = docId,
                Description = memo,
                Debit = debit,
                Credit = credit
            };

            _db.PartyLedgers.Add(row);
            await _db.SaveChangesAsync();
            // === SYNC: replicate this ledger entry ===
            await _outbox.EnqueueUpsertAsync(_db, row, default);
            await _db.SaveChangesAsync();


            // Update snapshot
            await UpsertBalanceAsync(partyId, ledgerOutletId, debit - credit);
        }

        private async Task UpsertBalanceAsync(int partyId, int? outletId, decimal delta)
        {
            var bal = await _db.PartyBalances
                .FirstOrDefaultAsync(b => b.PartyId == partyId && b.OutletId == outletId);

            if (bal == null)
            {
                bal = new PartyBalance
                {
                    PartyId = partyId,
                    OutletId = outletId,
                    Balance = 0,
                    AsOfUtc = DateTime.UtcNow   // match entity
                };
                _db.PartyBalances.Add(bal);
            }

            bal.Balance += delta;
            bal.AsOfUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
