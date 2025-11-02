using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Domain.Entities;
using System.Text;
using System.Windows;
using Pos.Domain;
using Pos.Client.Wpf.Services;

public sealed class TillService : ITillService
{
    private readonly DbContextOptions<PosClientDbContext> _dbOptions;
    private readonly ITerminalContext _ctx;
    private readonly IGlPostingService _gl;

    public TillService(DbContextOptions<PosClientDbContext> dbOptions, ITerminalContext ctx, IGlPostingService gl)
    {
        _dbOptions = dbOptions;
        _ctx = ctx;
        _gl = gl;
    }

    private int OutletId => _ctx.OutletId;
    private int CounterId => _ctx.CounterId;
    public async Task<bool> OpenTillAsync()
    {
        using var db = new PosClientDbContext(_dbOptions);
        var open = GetOpenTill(db, OutletId, CounterId);
        if (open != null)
        {
            MessageBox.Show($"Till already open (Id={open.Id}).", "Info");
            return false;
        }

        var session = new TillSession
        {
            OutletId = OutletId,
            CounterId = CounterId,
            OpenTs = DateTime.UtcNow,
            OpeningFloat = 0m
        };
        db.TillSessions.Add(session);
        await db.SaveChangesAsync();
        MessageBox.Show($"Till opened. Id={session.Id}", "Till");
        return true;
    }

    public async Task<bool> CloseTillAsync()
    {
        using var db = new PosClientDbContext(_dbOptions);

        var open = GetOpenTill(db, OutletId, CounterId);
        if (open == null)
        {
            MessageBox.Show("No open till to close.", "Info");
            return false;
        }

        // A) Latest state for business totals (exclude superseded & voided)
        var latest = db.Sales.AsNoTracking()
            .Where(s => s.TillSessionId == open.Id
                     && s.Status == SaleStatus.Final
                     && s.VoidedAtUtc == null
                     && s.RevisedToSaleId == null)
            .ToList();

        var latestSales = latest.Where(s => !s.IsReturn).ToList();
        var latestReturns = latest.Where(s => s.IsReturn).ToList();

        var salesTotal = latestSales.Sum(s => s.Total);
        var returnsTotalAbs = latestReturns.Sum(s => Math.Abs(s.Total));
        var netTotal = salesTotal - returnsTotalAbs;

        // B) Movements for expected cash (include ALL final, non-voided docs; each revision = delta)
        var moves = db.Sales.AsNoTracking()
            .Where(s => s.TillSessionId == open.Id
                     && s.Status == SaleStatus.Final
                     && s.VoidedAtUtc == null)
            .ToList();

        // Cash from sales: sign-preserving (amendments reducing cash will be negative)
        var salesCash = moves.Where(s => !s.IsReturn).Sum(s => s.CashAmount);

        // Cash refunds: subtract absolute magnitude (polarity-safe for how returns are stored)
        var refundsCashAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CashAmount));

        var expectedCash = open.OpeningFloat + salesCash - refundsCashAbs;

        // Prompt for declared cash (pre-fill with expected)
        var declaredStr = Microsoft.VisualBasic.Interaction.InputBox(
            $"Z Report\nSales total: {salesTotal:0.00}\nReturns total: {returnsTotalAbs:0.00}\nNet total: {netTotal:0.00}\n\nEnter DECLARED CASH:",
            "Close Till (Z Report)", expectedCash.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        if (!decimal.TryParse(declaredStr, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.CurrentCulture, out var declaredCash))
        {
            if (!decimal.TryParse(declaredStr, System.Globalization.NumberStyles.Number,
                                  System.Globalization.CultureInfo.InvariantCulture, out declaredCash))
            {
                MessageBox.Show("Invalid amount. Till not closed.");
                return false;
            }
        }

        var overShort = declaredCash - expectedCash;

        open.CloseTs = DateTime.UtcNow;
        open.DeclaredCash = declaredCash;
        open.OverShort = overShort;
        await db.SaveChangesAsync();

        // --- GL: move declared cash from Till -> Cash in Hand and post over/short ---
        var systemCash = salesCash - refundsCashAbs;                 // exclude opening float
        var declaredToMove = declaredCash - open.OpeningFloat;       // keep float in till
        if (declaredToMove < 0m) declaredToMove = 0m;

        await _gl.PostTillCloseAsync(open, declaredToMove, systemCash);

        var z = new StringBuilder();
        z.AppendLine($"=== Z REPORT (Till {open.Id}) ===");
        z.AppendLine($"Outlet/Counter : {OutletId}/{CounterId}");
        z.AppendLine($"Opened (local) : {open.OpenTs.ToLocalTime()}");
        z.AppendLine($"Closed (local) : {open.CloseTs?.ToLocalTime()}");
        z.AppendLine($"Opening Float  : {open.OpeningFloat:0.00}");
        z.AppendLine($"Sales Total    : {salesTotal:0.00}");
        z.AppendLine($"Returns Total  : {returnsTotalAbs:0.00}");
        z.AppendLine($"Net Total      : {netTotal:0.00}");
        z.AppendLine($"Expected Cash  : {expectedCash:0.00}");
        z.AppendLine($"Declared Cash  : {declaredCash:0.00}");
        z.AppendLine($"Over/Short     : {overShort:+0.00;-0.00;0.00}");

        MessageBox.Show(z.ToString(), "Z Report");
        return true;
    }



    public string GetStatusText()
    {
        using var db = new PosClientDbContext(_dbOptions);
        var open = GetOpenTill(db, OutletId, CounterId);
        return open == null
            ? "Till: Closed"
            : $"Till: OPEN (Id={open.Id}, Opened {open.OpenTs:HH:mm})";
    }

    public bool IsTillOpen()
    {
        using var db = new PosClientDbContext(_dbOptions);
        return GetOpenTill(db, OutletId, CounterId) != null;
    }

    // Note: now parameterized by outlet/counter
    private static TillSession? GetOpenTill(PosClientDbContext db, int outletId, int counterId)
        => db.TillSessions
             .OrderByDescending(t => t.Id)
             .FirstOrDefault(t => t.OutletId == outletId
                               && t.CounterId == counterId
                               && t.CloseTs == null);
}
