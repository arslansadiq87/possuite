using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Till;
using Pos.Domain.Services;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    /// <summary>EF-based implementation; no UI, no MessageBox. Clear messages in exceptions.</summary>
    public sealed class TillService : ITillService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ITerminalContext _ctx;
        private readonly IGlPostingService _gl;
        private readonly IOutboxWriter _outbox;

        public TillService(
            IDbContextFactory<PosClientDbContext> dbf,
            ITerminalContext ctx,
            IGlPostingService gl,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _ctx = ctx;
            _gl = gl;
            _outbox = outbox;
        }

        private int OutletId => _ctx.OutletId;
        private int CounterId => _ctx.CounterId;

        public async Task<TillOpenResultDto> OpenTillAsync(decimal openingFloat, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var existing = await GetOpenTillAsync(db, OutletId, CounterId, ct);
            if (existing is not null)
                throw new InvalidOperationException($"Till is already open (Id={existing.Id}).");

            var session = new TillSession
            {
                OutletId = OutletId,
                CounterId = CounterId,
                OpenTs = DateTime.UtcNow,
                OpeningFloat = openingFloat < 0 ? 0m : openingFloat
            };

            await db.TillSessions.AddAsync(session, ct);

            // Outbox before final save
            await _outbox.EnqueueUpsertAsync(db, session, ct);
            await db.SaveChangesAsync(ct);

            return new TillOpenResultDto
            {
                TillSessionId = session.Id,
                OpenedAtUtc = session.OpenTs,
                OpeningFloat = session.OpeningFloat
            };
        }

        public async Task<TillCloseResultDto> CloseTillAsync(decimal declaredCash, CancellationToken ct = default)
        {
            if (declaredCash < 0m)
                throw new ArgumentOutOfRangeException(nameof(declaredCash), "Declared cash cannot be negative.");

            await using var db = await _dbf.CreateDbContextAsync(ct);

            var open = await GetOpenTillAsync(db, OutletId, CounterId, ct);
            if (open is null)
                throw new InvalidOperationException("No open till to close.");

            // A) Latest state for business totals (exclude superseded & voided)
            var latest = await db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == open.Id
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null
                         && s.RevisedToSaleId == null)
                .ToListAsync(ct);

            var latestSales = latest.Where(s => !s.IsReturn).ToList();
            var latestReturns = latest.Where(s => s.IsReturn).ToList();

            var salesTotal = latestSales.Sum(s => s.Total);
            var returnsTotalAbs = latestReturns.Sum(s => Math.Abs(s.Total));
            var netTotal = salesTotal - returnsTotalAbs;

            // B) Movements for expected cash (include ALL final, non-voided docs; each revision = delta)
            var moves = await db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == open.Id
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null)
                .ToListAsync(ct);

            var salesCash = moves.Where(s => !s.IsReturn).Sum(s => s.CashAmount);
            var refundsCashAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CashAmount));

            var expectedCash = open.OpeningFloat + salesCash - refundsCashAbs;

            var declaredToMove = declaredCash - open.OpeningFloat; // keep float in till
            if (declaredToMove < 0m) declaredToMove = 0m;

            var systemCash = salesCash - refundsCashAbs;

            open.CloseTs = DateTime.UtcNow;
            open.DeclaredCash = declaredCash;
            open.OverShort = declaredCash - expectedCash;

            // Outbox before final save (session update)
            await _outbox.EnqueueUpsertAsync(db, open, ct);
            await db.SaveChangesAsync(ct);

            // GL after session persisted
            await _gl.PostTillCloseAsync(open, declaredToMove, systemCash, ct);

            return new TillCloseResultDto
            {
                TillSessionId = open.Id,
                ClosedAtUtc = open.CloseTs!.Value,
                SalesTotal = salesTotal,
                ReturnsTotalAbs = returnsTotalAbs,
                OpeningFloat = open.OpeningFloat,
                ExpectedCash = expectedCash,
                DeclaredCash = declaredCash,
                SystemCash = systemCash,
                DeclaredToMove = declaredToMove
            };
        }

        public async Task<TillStatusDto> GetStatusAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var open = await GetOpenTillAsync(db, OutletId, CounterId, ct);
            return new TillStatusDto
            {
                IsOpen = open != null,
                TillSessionId = open?.Id,
                OpenedAtUtc = open?.OpenTs
            };
        }

        public async Task<bool> IsTillOpenAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GetOpenTillAsync(db, OutletId, CounterId, ct) != null;
        }

        private static Task<TillSession?> GetOpenTillAsync(PosClientDbContext db, int outletId, int counterId, CancellationToken ct)
            => db.TillSessions
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync(t => t.OutletId == outletId
                                        && t.CounterId == counterId
                                        && t.CloseTs == null, ct);

        public async Task<TillClosePreviewDto> GetClosePreviewAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var open = await GetOpenTillAsync(db, OutletId, CounterId, ct);
            if (open is null)
                throw new InvalidOperationException("No open till to close.");

            // Latest state totals (exclude superseded & voided)
            var latest = await db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == open.Id
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null
                         && s.RevisedToSaleId == null)
                .ToListAsync(ct);

            var latestSales = latest.Where(s => !s.IsReturn).ToList();
            var latestReturns = latest.Where(s => s.IsReturn).ToList();

            var salesTotal = latestSales.Sum(s => s.Total);
            var returnsTotalAbs = latestReturns.Sum(s => Math.Abs(s.Total));

            // Movements for expected cash (include all final non-voided docs)
            var moves = await db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == open.Id
                         && s.Status == SaleStatus.Final
                         && s.VoidedAtUtc == null)
                .ToListAsync(ct);

            var salesCash = moves.Where(s => !s.IsReturn).Sum(s => s.CashAmount);
            var refundsCashAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CashAmount));

            var expectedCash = open.OpeningFloat + salesCash - refundsCashAbs;
            var systemCash = salesCash - refundsCashAbs;

            return new TillClosePreviewDto
            {
                OpeningFloat = open.OpeningFloat,
                SalesTotal = salesTotal,
                ReturnsTotalAbs = returnsTotalAbs,
                ExpectedCash = expectedCash,
                SystemCash = systemCash
            };
        }
    }
}
