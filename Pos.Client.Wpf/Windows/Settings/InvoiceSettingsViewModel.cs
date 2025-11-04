// Pos.Client.Wpf/Windows/Settings/InvoiceSettingsViewModel.cs
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Microsoft.Win32;
using System.IO;
using Pos.Client.Wpf.Printing;
namespace Pos.Client.Wpf.Windows.Settings;

public partial class InvoiceSettingsViewModel : ObservableObject
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<string> InstalledPrinters { get; } =
        new(PrinterSettings.InstalledPrinters.Cast<string>());

    public int[] PaperWidths { get; } = new[] { 58, 80 };
    public string[] Languages { get; } = new[] { "en", "ur", "ar" };
    [ObservableProperty] private string previewText = "";

    [ObservableProperty] private bool isGlobal = true;
    [ObservableProperty] private Outlet? selectedOutlet;

    [ObservableProperty] private string? outletDisplayName;
    [ObservableProperty] private string? addressLine1;
    [ObservableProperty] private string? addressLine2;
    [ObservableProperty] private string? phone;

    [ObservableProperty] private string? printerName;
    [ObservableProperty] private int paperWidthMm = 80;
    [ObservableProperty] private bool enableDrawerKick = true;

    [ObservableProperty] private bool printOnSave;
    [ObservableProperty] private bool askToPrintOnSave;
    [ObservableProperty] private bool printBarcodeOnReceipt;

    [ObservableProperty] private byte[]? logoPng;
    [ObservableProperty] private int logoMaxWidthPx = 384;
    [ObservableProperty] private string logoAlignment = "Center";

    [ObservableProperty] private bool showQr = false;
    [ObservableProperty] private bool showCustomerOnReceipt = true;
    [ObservableProperty] private bool showCashierOnReceipt = true;

    [ObservableProperty] private string selectedLang = "en";
    [ObservableProperty] private InvoiceLocalization currentLocalization = new() { Lang = "en" };

    // Identity & FBR/NTN
    [ObservableProperty] private string? businessNtn;
    [ObservableProperty] private bool showBusinessNtn;
    [ObservableProperty] private bool enableFbr;
    [ObservableProperty] private bool showFbrQr;
    [ObservableProperty] private string? fbrPosId;
    [ObservableProperty] private string? fbrApiBaseUrl;
    [ObservableProperty] private string? fbrAuthKey;

    [ObservableProperty] private bool showBusinessName = true;
    [ObservableProperty] private bool showAddress = true;
    [ObservableProperty] private bool showContacts = true;
    [ObservableProperty] private bool showLogo = false;

    // Row visibility
    [ObservableProperty] private bool rowShowProductName = true;
    [ObservableProperty] private bool rowShowProductSku = false;
    [ObservableProperty] private bool rowShowQty = true;
    [ObservableProperty] private bool rowShowUnitPrice = true;
    [ObservableProperty] private bool rowShowLineDiscount = false;
    [ObservableProperty] private bool rowShowLineTotal = true;

    // Totals
    [ObservableProperty] private bool totalsShowTaxes = true;
    [ObservableProperty] private bool totalsShowDiscounts = true;
    [ObservableProperty] private bool totalsShowOtherExpenses = true;
    [ObservableProperty] private bool totalsShowGrandTotal = true;
    [ObservableProperty] private bool totalsShowPaymentRecv = true;
    [ObservableProperty] private bool totalsShowBalance = true;

    // Footer
    [ObservableProperty] private bool showFooter = true;
    // ADD — bank defaults
    [ObservableProperty] private Account? selectedPurchaseBankAccount;
    [ObservableProperty] private Account? selectedSalesCardClearingAccount;

    // Bank list for the picker
    public ObservableCollection<Account> BankAccounts { get; } = new();


    private InvoiceSettings? _loadedSettings;
    private List<InvoiceLocalization> _loadedLocs = new();


    public InvoiceSettingsViewModel(IDbContextFactory<PosClientDbContext> dbf)
    {
        _dbf = dbf;
        _ = InitAsync();
        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PaperWidthMm):
                case nameof(ShowLogo):
                case nameof(ShowBusinessName):
                case nameof(ShowAddress):
                case nameof(ShowContacts):
                case nameof(ShowBusinessNtn):
                case nameof(EnableFbr):
                case nameof(ShowFbrQr):
                case nameof(RowShowProductName):
                case nameof(RowShowProductSku):
                case nameof(RowShowQty):
                case nameof(RowShowUnitPrice):
                case nameof(RowShowLineDiscount):
                case nameof(RowShowLineTotal):
                case nameof(TotalsShowTaxes):
                case nameof(TotalsShowDiscounts):
                case nameof(TotalsShowOtherExpenses):
                case nameof(TotalsShowGrandTotal):
                case nameof(TotalsShowPaymentRecv):
                case nameof(TotalsShowBalance):
                case nameof(ShowFooter):
                case nameof(OutletDisplayName):
                case nameof(AddressLine1):
                case nameof(AddressLine2):
                case nameof(Phone):
                case nameof(BusinessNtn):
                    _ = RenderPreviewAsync();
                    break;
            }
        };
    }

    private async Task InitAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var outlets = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        foreach (var o in outlets) Outlets.Add(o);

        await LoadAsync();
        UpdateCurrentLocalization();
    }

    partial void OnSelectedLangChanged(string value) => UpdateCurrentLocalization();
    partial void OnIsGlobalChanged(bool value) => _ = LoadAsync();
    partial void OnSelectedOutletChanged(Outlet? value) { if (!IsGlobal) _ = LoadAsync(); }

    private async Task LoadAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();

        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;

        _loadedSettings = await db.InvoiceSettings
            .Include(x => x.Localizations)
            .Where(x => x.OutletId == outletId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync();

        if (_loadedSettings == null)
            _loadedSettings = new InvoiceSettings { OutletId = outletId };

        _loadedLocs = _loadedSettings.Localizations?.ToList() ?? new();

        // snapshot to fields
        OutletDisplayName = _loadedSettings.OutletDisplayName;
        AddressLine1 = _loadedSettings.AddressLine1;
        AddressLine2 = _loadedSettings.AddressLine2;
        Phone = _loadedSettings.Phone;
        PrinterName = _loadedSettings.PrinterName;
        PaperWidthMm = _loadedSettings.PaperWidthMm <= 0 ? 80 : _loadedSettings.PaperWidthMm;
        EnableDrawerKick = _loadedSettings.EnableDrawerKick;
        ShowQr = _loadedSettings.ShowQr;
        ShowCustomerOnReceipt = _loadedSettings.ShowCustomerOnReceipt;
        ShowCashierOnReceipt = _loadedSettings.ShowCashierOnReceipt;

        // --- Bank defaults (Sales card clearing / Purchase bank) ---
        await LoadBankAccountsAsync(outletId);
        SelectedPurchaseBankAccount = (_loadedSettings.PurchaseBankAccountId is int pid)
            ? BankAccounts.FirstOrDefault(a => a.Id == pid)
            : null;
        SelectedSalesCardClearingAccount = (_loadedSettings.SalesCardClearingAccountId is int sid)
            ? BankAccounts.FirstOrDefault(a => a.Id == sid)
            : null;
        // ------------------------------------------------------------

        PrintOnSave = _loadedSettings.PrintOnSave;
        AskToPrintOnSave = _loadedSettings.AskToPrintOnSave;
        PrintBarcodeOnReceipt = _loadedSettings.PrintBarcodeOnReceipt;

        LogoPng = _loadedSettings.LogoPng;
        LogoMaxWidthPx = _loadedSettings.LogoMaxWidthPx <= 0 ? 384 : _loadedSettings.LogoMaxWidthPx;
        LogoAlignment = string.IsNullOrWhiteSpace(_loadedSettings.LogoAlignment) ? "Center" : _loadedSettings.LogoAlignment;
        BusinessNtn = _loadedSettings.BusinessNtn;
        ShowBusinessNtn = _loadedSettings.ShowBusinessNtn;
        EnableFbr = _loadedSettings.EnableFbr;
        ShowFbrQr = _loadedSettings.ShowFbrQr;
        FbrPosId = _loadedSettings.FbrPosId;
        FbrApiBaseUrl = _loadedSettings.FbrApiBaseUrl;
        FbrAuthKey = _loadedSettings.FbrAuthKey;

        ShowBusinessName = _loadedSettings.ShowBusinessName;
        ShowAddress = _loadedSettings.ShowAddress;
        ShowContacts = _loadedSettings.ShowContacts;
        ShowLogo = _loadedSettings.ShowLogo;

        RowShowProductName = _loadedSettings.RowShowProductName;
        RowShowProductSku = _loadedSettings.RowShowProductSku;
        RowShowQty = _loadedSettings.RowShowQty;
        RowShowUnitPrice = _loadedSettings.RowShowUnitPrice;
        RowShowLineDiscount = _loadedSettings.RowShowLineDiscount;
        RowShowLineTotal = _loadedSettings.RowShowLineTotal;

        TotalsShowTaxes = _loadedSettings.TotalsShowTaxes;
        TotalsShowDiscounts = _loadedSettings.TotalsShowDiscounts;
        TotalsShowOtherExpenses = _loadedSettings.TotalsShowOtherExpenses;
        TotalsShowGrandTotal = _loadedSettings.TotalsShowGrandTotal;
        TotalsShowPaymentRecv = _loadedSettings.TotalsShowPaymentRecv;
        TotalsShowBalance = _loadedSettings.TotalsShowBalance;

        ShowFooter = _loadedSettings.ShowFooter;

        UpdateCurrentLocalization();
        await RenderPreviewAsync();

    }

    private async Task LoadBankAccountsAsync(int? outletId)
    {
        BankAccounts.Clear();
        await using var db = await _dbf.CreateDbContextAsync();
        var q = db.Accounts.AsNoTracking()
            .Where(a => a.AllowPosting && !a.IsHeader);

        // Prefer outlet-scoped accounts when editing outlet-scoped settings
        if (outletId != null)
            q = q.Where(a => a.OutletId == outletId);
        else
            q = q.Where(a => a.OutletId == null);

        // Heuristic: same you used in PurchaseView (Name contains “Bank” or code prefix)
        q = q.Where(a => a.Name.Contains("Bank") || a.Code.StartsWith("101"));

        var list = await q.OrderBy(a => a.Code).ThenBy(a => a.Name).ToListAsync();
        foreach (var a in list) BankAccounts.Add(a);
    }


    private void UpdateCurrentLocalization()
    {
        var loc = _loadedLocs.FirstOrDefault(x => x.Lang == SelectedLang);
        if (loc == null)
        {
            loc = new InvoiceLocalization { Lang = SelectedLang, Footer = "Thank you for shopping with us!" };
            _loadedLocs.Add(loc);
        }
        CurrentLocalization = loc;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();

        // attach or add
        var isNew = _loadedSettings!.Id == 0;
        _loadedSettings.OutletDisplayName = OutletDisplayName;
        _loadedSettings.AddressLine1 = AddressLine1;
        _loadedSettings.AddressLine2 = AddressLine2;
        _loadedSettings.Phone = Phone;
        _loadedSettings.PrinterName = PrinterName;
        _loadedSettings.PaperWidthMm = PaperWidthMm;
        _loadedSettings.EnableDrawerKick = EnableDrawerKick;
        _loadedSettings.ShowQr = ShowQr;
        _loadedSettings.ShowCustomerOnReceipt = ShowCustomerOnReceipt;
        _loadedSettings.ShowCashierOnReceipt = ShowCashierOnReceipt;
        _loadedSettings.UpdatedAtUtc = DateTime.UtcNow;
        _loadedSettings.OutletId = IsGlobal ? null : SelectedOutlet?.Id;
        _loadedSettings.PrintOnSave = PrintOnSave;
        _loadedSettings.AskToPrintOnSave = AskToPrintOnSave;
        _loadedSettings.PrintBarcodeOnReceipt = PrintBarcodeOnReceipt;

        _loadedSettings.LogoPng = LogoPng;
        _loadedSettings.LogoMaxWidthPx = LogoMaxWidthPx;
        _loadedSettings.LogoAlignment = LogoAlignment;

        _loadedSettings.BusinessNtn = BusinessNtn;
        _loadedSettings.ShowBusinessNtn = ShowBusinessNtn;
        _loadedSettings.EnableFbr = EnableFbr;
        _loadedSettings.ShowFbrQr = ShowFbrQr;
        _loadedSettings.FbrPosId = FbrPosId;
        _loadedSettings.FbrApiBaseUrl = FbrApiBaseUrl;
        _loadedSettings.FbrAuthKey = FbrAuthKey;

        _loadedSettings.ShowBusinessName = ShowBusinessName;
        _loadedSettings.ShowAddress = ShowAddress;
        _loadedSettings.ShowContacts = ShowContacts;
        _loadedSettings.ShowLogo = ShowLogo;

        _loadedSettings.RowShowProductName = RowShowProductName;
        _loadedSettings.RowShowProductSku = RowShowProductSku;
        _loadedSettings.RowShowQty = RowShowQty;
        _loadedSettings.RowShowUnitPrice = RowShowUnitPrice;
        _loadedSettings.RowShowLineDiscount = RowShowLineDiscount;
        _loadedSettings.RowShowLineTotal = RowShowLineTotal;

        _loadedSettings.TotalsShowTaxes = TotalsShowTaxes;
        _loadedSettings.TotalsShowDiscounts = TotalsShowDiscounts;
        _loadedSettings.TotalsShowOtherExpenses = TotalsShowOtherExpenses;
        _loadedSettings.TotalsShowGrandTotal = TotalsShowGrandTotal;
        _loadedSettings.TotalsShowPaymentRecv = TotalsShowPaymentRecv;
        _loadedSettings.TotalsShowBalance = TotalsShowBalance;

        _loadedSettings.ShowFooter = ShowFooter;
        // --- Save bank defaults ---
        _loadedSettings.PurchaseBankAccountId = SelectedPurchaseBankAccount?.Id;
        _loadedSettings.SalesCardClearingAccountId = SelectedSalesCardClearingAccount?.Id;
        // ---------------------------

        if (isNew) db.InvoiceSettings.Add(_loadedSettings);
        else db.InvoiceSettings.Update(_loadedSettings);

        foreach (var loc in _loadedLocs)
        {
            if (loc.Id == 0) { loc.InvoiceSettings = _loadedSettings; db.Set<InvoiceLocalization>().Add(loc); }
            else db.Set<InvoiceLocalization>().Update(loc);

        }

        await db.SaveChangesAsync();

        // Optionally: toast/snackbar
    }

    [RelayCommand]
    private async Task ResetAsync() => await LoadAsync();
    [RelayCommand]
    private void RemoveLogo() => LogoPng = null;

    [RelayCommand]
    private void UploadLogo()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG Images|*.png",
            Title = "Select Logo (PNG)"
        };
        if (dlg.ShowDialog() == true)
        {
            LogoPng = File.ReadAllBytes(dlg.FileName);
        }
    }


    private async Task RenderPreviewAsync()
    {
        try
        {
            await using var db = await _dbf.CreateDbContextAsync();
            var items = await db.Items
                .AsNoTracking()
                .Select(x => new { x.Name, x.Sku })
                .Take(4)
                .ToListAsync();

            var lines = items.Select((x, i) => new ReceiptPreviewLine
            {
                Name = string.IsNullOrWhiteSpace(x.Name) ? $"Item {i + 1}" : x.Name!,
                Sku = string.IsNullOrWhiteSpace(x.Sku) ? $"SKU{i + 1:D3}" : x.Sku!,
                Qty = (i % 3) + 1,
                Unit = 99.00m + i * 25m,
                LineDiscount = (i == 0) ? 10m : 0m
            }).ToList();

            if (lines.Count == 0)
            {
                lines = Enumerable.Range(1, 3).Select(i => new ReceiptPreviewLine
                {
                    Name = $"Sample Item {i}",
                    Sku = $"SKU{i:000}",
                    Qty = (i % 3) + 1,
                    Unit = 99.00m + i * 25m,
                    LineDiscount = (i == 1) ? 5m : 0m
                }).ToList();
            }

            // Mock meta for preview
            var sale = new ReceiptPreviewSale
            {
                // totals
                Subtotal = lines.Sum(l => l.Qty * l.Unit),
                InvoiceDiscount = 20m,
                Tax = 30m,
                OtherExpenses = 0m,
                Paid = 500m,

                // meta
                Ts = DateTime.Now,
                OutletId = SelectedOutlet?.Id,
                OutletCode = SelectedOutlet?.Code ?? $"OUT-{(SelectedOutlet?.Id ?? 1):000}", // if Code not present, this fallback is fine
                CounterId = 1,
                CounterName = "Front Counter",
                InvoiceNumber = 1234,
                CashierName = ShowCashierOnReceipt ? "Cashier A" : null,

                // placeholders for barcode/QR
                BarcodeText = "123456789012",
                QrText = ShowQr ? "https://example.com/e-receipt/ABCDEF" : null
            };
            sale.Total = sale.Subtotal - sale.InvoiceDiscount + sale.Tax + sale.OtherExpenses;
            sale.Balance = Math.Max(0m, sale.Total - sale.Paid);

            int width = PaperWidthMm <= 58 ? 32 : 42;

            PreviewText = ReceiptPreviewBuilder.BuildText(
                width,
                // identity block
                ShowBusinessName ? OutletDisplayName : null,
                ShowAddress ? $"{AddressLine1}\n{AddressLine2}".Trim() : null,
                ShowContacts ? Phone : null,
                ShowBusinessNtn ? BusinessNtn : null,
                // logo flag
                ShowLogo,
                // item row flags (note: qty now appears inline between name and unit)
                RowShowProductName, RowShowProductSku, RowShowQty, RowShowUnitPrice, RowShowLineDiscount, RowShowLineTotal,
                // totals flags (TOTAL & BALANCE rendered emphasized in builder)
                TotalsShowTaxes, TotalsShowDiscounts, TotalsShowOtherExpenses, TotalsShowGrandTotal, TotalsShowPaymentRecv, TotalsShowBalance,
                // footer text
                ShowFooter ? CurrentLocalization?.Footer : null,
                // FBR
                EnableFbr, ShowFbrQr, FbrPosId,
                // data
                lines, sale,
                // generic barcode/QR toggles
                showBarcodeOnReceipt: PrintBarcodeOnReceipt,
                showGenericQr: ShowQr
            );
        }
        catch (Exception ex)
        {
            int width = PaperWidthMm <= 58 ? 32 : 42;
            PreviewText = $"[Preview error]\n{ex.Message}\n\n{new string('-', width)}\n";
        }
    }








}
