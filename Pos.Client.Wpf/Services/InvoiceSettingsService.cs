// Pos.Client.Wpf/Services/InvoiceSettingsService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using System.Threading; // ← ensure this is present (for CancellationToken)

namespace Pos.Client.Wpf.Services;

public interface IInvoiceSettingsService
{
    Task<(InvoiceSettings Settings, InvoiceLocalization Local)> GetAsync(
        int? outletId, string? lang, CancellationToken ct = default);

    Task<string?> GetPrinterAsync(int? outletId, CancellationToken ct = default);
    Task<int> GetPaperWidthAsync(int? outletId, CancellationToken ct = default);
    // >>> ADD THESE TWO LINES <<<
    Task<int?> GetSalesCardClearingAccountIdAsync(int? outletId, CancellationToken ct = default);
    Task<int?> GetPurchaseBankAccountIdAsync(int? outletId, CancellationToken ct = default);
}

public class InvoiceSettingsService : IInvoiceSettingsService
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;

    public InvoiceSettingsService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

    public async Task<int?> GetSalesCardClearingAccountIdAsync(int? outletId, CancellationToken ct = default)
    {
        var (s, _) = await GetAsync(outletId, "en", ct);
        return s.SalesCardClearingAccountId;
    }

    public async Task<int?> GetPurchaseBankAccountIdAsync(int? outletId, CancellationToken ct = default)
    {
        var (s, _) = await GetAsync(outletId, "en", ct);
        return s.PurchaseBankAccountId;
    }

    public async Task<(InvoiceSettings, InvoiceLocalization)> GetAsync(
        int? outletId, string? lang, CancellationToken ct = default)
    {
        lang ??= "en";

        await using var db = await _dbf.CreateDbContextAsync(ct);

        // 1) Outlet row (if any)
        var outletRow = await db.InvoiceSettings
            .Include(x => x.Localizations)
            .Where(x => x.OutletId == outletId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        // 2) Global row
        var globalRow = await db.InvoiceSettings
            .Include(x => x.Localizations)
            .Where(x => x.OutletId == null)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var settings = outletRow ?? globalRow ?? new InvoiceSettings();
        var loc = (settings.Localizations.FirstOrDefault(x => x.Lang == lang)
                  ?? settings.Localizations.FirstOrDefault(x => x.Lang == "en")
                  ?? new InvoiceLocalization { Lang = lang, Footer = "Thank you for shopping with us!" });

        return (settings, loc);
    }

    public async Task<string?> GetPrinterAsync(int? outletId, CancellationToken ct = default)
    {
        var (s, _) = await GetAsync(outletId, "en", ct);
        return s.PrinterName;
    }

    public async Task<int> GetPaperWidthAsync(int? outletId, CancellationToken ct = default)
    {
        var (s, _) = await GetAsync(outletId, "en", ct);
        return s.PaperWidthMm <= 0 ? 80 : s.PaperWidthMm;
    }
}
