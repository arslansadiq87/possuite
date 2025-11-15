// Pos.Persistence/Services/GlReadService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Services;
using Pos.Persistence;

public sealed class GlReadService : IGlReadService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    public GlReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    public async Task<decimal> GetApBalanceForPurchaseAsync(
        Guid purchasePublicId, int supplierAccountId, CancellationToken ct)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        // Sum only the Supplier account legs for this purchase (all revisions),
        // keyed by PublicId (or use DocNo if that’s your invariant).
        var (cr, dr) = await db.GlEntries
            .Where(e => e.PublicId == purchasePublicId && e.AccountId == supplierAccountId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cr = g.Sum(x => (decimal?)x.Credit) ?? 0m,
                Dr = g.Sum(x => (decimal?)x.Debit) ?? 0m
            })
            .Select(x => new ValueTuple<decimal, decimal>(x.Cr, x.Dr))
            .FirstOrDefaultAsync(ct);

        // AP balance = Credits (billings) – Debits (payments & downward amendments)
        // > 0  => We owe supplier (balance due)
        // = 0  => Settled
        // < 0  => Supplier owes us (credit)
        return cr - dr;
    }
}
