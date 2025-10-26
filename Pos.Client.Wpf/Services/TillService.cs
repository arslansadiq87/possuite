using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Domain.Entities;
using System.Text;
using System.Windows;
using Pos.Domain;

public sealed class TillService : ITillService
{
    private readonly DbContextOptions<PosClientDbContext> _dbOptions;
    private const int OutletId = 1;
    private const int CounterId = 1;

    public TillService(DbContextOptions<PosClientDbContext> dbOptions)
    {
        _dbOptions = dbOptions;
    }

    public async Task<bool> OpenTillAsync()
    {
        using var db = new PosClientDbContext(_dbOptions);
        var open = GetOpenTill(db);
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
        var open = GetOpenTill(db);
        if (open == null)
        {
            MessageBox.Show("No open till to close.", "Info");
            return false;
        }

        var all = db.Sales
            .Where(s => s.TillSessionId == open.Id && s.Status == SaleStatus.Final)  // SaleStatus is in Pos.Domain
            .AsNoTracking()
            .ToList();

        var sales = all.Where(s => !s.IsReturn).ToList();
        var returns = all.Where(s => s.IsReturn).ToList();
        var salesTotal = sales.Sum(s => s.Total);
        var returnsTotal = returns.Sum(s => s.Total);
        var expectedCash = open.OpeningFloat + sales.Sum(s => s.CashAmount) - returns.Sum(s => s.CashAmount);

        var declaredStr = Microsoft.VisualBasic.Interaction.InputBox(
            $"Z Report\nSales total: {salesTotal:0.00}\n\nEnter DECLARED CASH:",
            "Close Till (Z Report)", expectedCash.ToString("0.00"));

        if (!decimal.TryParse(declaredStr, out var declaredCash))
        {
            MessageBox.Show("Invalid amount. Till not closed.");
            return false;
        }

        var overShort = declaredCash - expectedCash;
        open.CloseTs = DateTime.UtcNow;
        open.DeclaredCash = declaredCash;
        open.OverShort = overShort;
        await db.SaveChangesAsync();

        var z = new StringBuilder();
        z.AppendLine($"=== Z REPORT (Till {open.Id}) ===");
        z.AppendLine($"Outlet/Counter: {OutletId}/{CounterId}");
        z.AppendLine($"Opened (local): {open.OpenTs.ToLocalTime()}");
        z.AppendLine($"Closed (local): {open.CloseTs?.ToLocalTime()}");
        z.AppendLine($"Opening Float : {open.OpeningFloat:0.00}");
        z.AppendLine($"Sales Total   : {salesTotal:0.00}");
        z.AppendLine($"Expected Cash : {expectedCash:0.00}");
        z.AppendLine($"Declared Cash : {declaredCash:0.00}");
        z.AppendLine($"Over/Short    : {overShort:+0.00;-0.00;0.00}");
        MessageBox.Show(z.ToString(), "Z Report");

        return true;
    }

    public string GetStatusText()
    {
        using var db = new PosClientDbContext(_dbOptions);
        var open = GetOpenTill(db);
        return open == null
            ? "Till: Closed"
            : $"Till: OPEN (Id={open.Id}, Opened {open.OpenTs:HH:mm})";
    }

    // NEW: parameterless IsTillOpen for DashboardVm
    public bool IsTillOpen()
    {
        using var db = new PosClientDbContext(_dbOptions);
        return GetOpenTill(db) != null;
    }

    private static TillSession? GetOpenTill(PosClientDbContext db)
        => db.TillSessions
             .OrderByDescending(t => t.Id)
             .FirstOrDefault(t => t.OutletId == OutletId
                               && t.CounterId == CounterId
                               && t.CloseTs == null);
}
