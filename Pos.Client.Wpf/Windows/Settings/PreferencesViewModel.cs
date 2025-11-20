using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly ILookupService _lookup;
    private readonly IUserPreferencesService _svc;
    private readonly IInvoiceSettingsService _invoiceSettings;

    // === Time zone / scope bits you already had ===
    [ObservableProperty] private bool canEditOutletPayments; // Admin/Manager only

    public ObservableCollection<TimeZoneInfo> TimeZones { get; } = new();
    [ObservableProperty] private string? selectedTimeZoneId;

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<Warehouse> Warehouses { get; } = new();
    public string[] BarcodeTypes { get; } = new[] { "EAN13", "Code128", "QR" };

    // === New: per-outlet payments (moved from Invoice Settings page) ===
    public ObservableCollection<Account> BankAccounts { get; } = new();

    [ObservableProperty] private Outlet? selectedOutlet;
    [ObservableProperty] private bool useTill = true;

    [ObservableProperty] private Account? selectedSalesCardClearingAccount;
    [ObservableProperty] private Account? selectedPurchaseBankAccount;

    // === Bound fields you already had ===
    [ObservableProperty] private string purchaseDestinationScope = "Outlet"; // "Outlet" | "Warehouse"
    [ObservableProperty] private int? purchaseDestinationId;
    [ObservableProperty] private string defaultBarcodeType = "EAN13";
    

    [ObservableProperty] private bool isBusy;

    public PreferencesViewModel(
        ILookupService lookup,
        IUserPreferencesService svc,
        IInvoiceSettingsService invoiceSettings)
    {
        _lookup = lookup;
        _svc = svc;
        _invoiceSettings = invoiceSettings;
    }

    // CommunityToolkit generates a partial On<Property>Changed hook for [ObservableProperty] fields.
    // This signature must EXACTLY match the generated one.
    partial void OnSelectedOutletChanged(Outlet? value)
    {
        _ = Task.Run(async () =>
        {
            CanEditOutletPayments = await Pos.Client.Wpf.Security.AuthZ.IsManagerOrAboveAsync();
            await ReloadOutletPaymentsAsync();
        });
    }

    // ---------- Commands ----------
    [RelayCommand] public Task ReloadOutletPayments() => ReloadOutletPaymentsAsync();
    

    // ---------- Private helpers ----------
    private async Task LoadBankAccountsAsync(int? outletId, CancellationToken ct = default)
    {
        BankAccounts.Clear();

        // Reuse the same approach as InvoiceSettingsViewModel: get ALL accounts and filter,
        // since ILookupService exposes GetAccountsAsync (not GetBankAccountsAsync).
        var all = await _lookup.GetAccountsAsync(outletId, ct);

        var list = all
            .Where(a => a.AllowPosting && !a.IsHeader)
            .Where(a => (outletId != null ? a.OutletId == outletId : a.OutletId == null))
            .Where(a =>
                   (a.Code?.StartsWith("113") == true) // common "Bank" code-class in your CoA
                || (a.Name?.Contains("Bank") ?? false)
                || (a.Name?.Contains("Card") ?? false)
                || (a.Name?.Contains("Clearing") ?? false))
            .OrderBy(a => a.Code)
            .ThenBy(a => a.Name)
            .ToList();

        foreach (var a in list) BankAccounts.Add(a);
    }

    private async Task ReloadOutletPaymentsAsync(CancellationToken ct = default)
    {
        if (SelectedOutlet == null) return;

        // Load accounts first (for dropdowns)
        await LoadBankAccountsAsync(SelectedOutlet.Id, ct);

        // Read per-outlet invoice settings
        var tuple = await _invoiceSettings.GetAsync(SelectedOutlet.Id, "en", ct);
        var settings = tuple.Settings;

        UseTill = settings.UseTill;

        // Map selected accounts
        SelectedSalesCardClearingAccount = (settings.SalesCardClearingAccountId is int sid)
            ? BankAccounts.FirstOrDefault(a => a.Id == sid)
            : null;

        SelectedPurchaseBankAccount = (settings.PurchaseBankAccountId is int pid)
            ? BankAccounts.FirstOrDefault(a => a.Id == pid)
            : null;
    }

    private async Task SaveOutletPaymentsAsync(CancellationToken ct = default)
    {
        if (SelectedOutlet == null) return;

        // Load existing row to avoid clobbering unrelated fields
        var tuple = await _invoiceSettings.GetAsync(SelectedOutlet.Id, "en", ct);
        var settings = tuple.Settings;
        var local = tuple.Local;

        // Update only the fields we manage here
        settings.UseTill = UseTill;
        settings.SalesCardClearingAccountId = SelectedSalesCardClearingAccount?.Id;
        settings.PurchaseBankAccountId = SelectedPurchaseBankAccount?.Id;

        await _invoiceSettings.SaveAsync(settings, new[] { local }, ct);

        MessageBox.Show("Outlet till & payment preferences saved.",
            "Preferences", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- Your existing load/save ----------
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Outlets.Clear();
            Warehouses.Clear();

            var outlets = await _lookup.GetOutletsAsync();
            foreach (var o in outlets.OrderBy(x => x.Name)) Outlets.Add(o);

            var warehouses = await _lookup.GetWarehousesAsync();
            foreach (var w in warehouses.OrderBy(x => x.Name)) Warehouses.Add(w);

            var p = await _svc.GetAsync();
            PurchaseDestinationScope = p.PurchaseDestinationScope ?? "Outlet";
            PurchaseDestinationId = p.PurchaseDestinationId;
            DefaultBarcodeType = string.IsNullOrWhiteSpace(p.DefaultBarcodeType) ? "EAN13" : p.DefaultBarcodeType;
            

            // Time zones
            TimeZones.Clear();
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
                TimeZones.Add(tz);

            SelectedTimeZoneId = string.IsNullOrWhiteSpace(p.DisplayTimeZoneId)
                ? TimeZoneInfo.Local.Id
                : p.DisplayTimeZoneId;

            // Pick first outlet by default and load its payments/till prefs
            if (Outlets.Count > 0 && SelectedOutlet == null)
                SelectedOutlet = Outlets[0];

            await ReloadOutletPaymentsAsync();
            CanEditOutletPayments = await Pos.Client.Wpf.Security.AuthZ.IsManagerOrAboveAsync();

        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        // Persist per-outlet Till & Payment settings along with other preferences
        if (SelectedOutlet != null && CanEditOutletPayments)
        {
            await SaveOutletPaymentsAsync();
        }
        try
        {
            var p = new UserPreference
            {
                PurchaseDestinationScope = PurchaseDestinationScope,
                PurchaseDestinationId = PurchaseDestinationId,
                DefaultBarcodeType = DefaultBarcodeType,
                DisplayTimeZoneId = SelectedTimeZoneId
            };

            await _svc.SaveAsync(p);

            // OS/UI concern: stays in Client
            Pos.Client.Wpf.Services.TimeService.SetTimeZone(SelectedTimeZoneId);

            MessageBox.Show("Preferences saved.",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { IsBusy = false; }
    }
}
