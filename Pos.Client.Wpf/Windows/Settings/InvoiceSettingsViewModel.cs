// Pos.Client.Wpf/Windows/Settings/InvoiceSettingsViewModel.cs
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Domain.Entities;
using System.IO;
using Pos.Client.Wpf.Printing;
using System;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class InvoiceSettingsViewModel : ObservableObject
{
    private readonly IInvoiceSettingsService _svc;
    private readonly ILookupService _lookup;

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<string> InstalledPrinters { get; } =
        new(PrinterSettings.InstalledPrinters.Cast<string>());

    public int[] PaperWidths { get; } = new[] { 58, 80 };
    public string[] Languages { get; } = new[] { "en", "ur", "ar" };
    [ObservableProperty] private string previewText = "";

    [ObservableProperty] private bool isGlobal = true;
    [ObservableProperty] private Outlet? selectedOutlet;
    // Keep Global/Outlet radios in sync (your XAML binds the second radio to IsOutlet)
    [ObservableProperty] private bool isOutlet;

    partial void OnIsOutletChanged(bool value)
    {
        // Flip IsGlobal when user selects Outlet scope
        if (IsGlobal == value) IsGlobal = !value;
    }


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


    public InvoiceSettingsViewModel(
    IInvoiceSettingsService svc,
    ILookupService lookup)
    {
        _svc = svc;
        _lookup = lookup;
        _ = InitAsync();
        IsOutlet = !IsGlobal; // initialize radio sync

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
        var outlets = await _lookup.GetOutletsAsync();
        Outlets.Clear();
        foreach (var o in outlets) Outlets.Add(o);
        await LoadAsync();
        UpdateCurrentLocalization();
    }

    partial void OnSelectedLangChanged(string value) => UpdateCurrentLocalization();
    partial void OnIsGlobalChanged(bool value) => _ = LoadAsync();
    partial void OnSelectedOutletChanged(Outlet? value) { if (!IsGlobal) _ = LoadAsync(); }

    private async Task LoadAsync()
    {
        

        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;

        var tuple = await _svc.GetAsync(outletId, SelectedLang);
        _loadedSettings = tuple.Settings;
         // keep the VM's local list for editing:
        _loadedLocs = _loadedSettings.Localizations?.ToList() ?? new();
         // also track current localization selection:
        CurrentLocalization = tuple.Local;
    
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
     
        var all = await _lookup.GetAccountsAsync(outletId);
        var list = all
               .Where(a => a.AllowPosting && !a.IsHeader)
               .Where(a => (outletId != null ? a.OutletId == outletId : a.OutletId == null))
               .Where(a => a.Code?.StartsWith("113") == true
                        || (a.Name?.Contains("Bank") ?? false)
                        || (a.Name?.Contains("Card") ?? false)
                        || (a.Name?.Contains("Clearing") ?? false))
               .OrderBy(a => a.Code)
               .ThenBy(a => a.Name)
               .ToList();
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
        // ---------------------------

        await _svc.SaveAsync(_loadedSettings, _loadedLocs);

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


    private Task RenderPreviewAsync()
    {
        try
        {
            // Preview should not depend on DB. Use mock lines.
            var lines = Enumerable.Range(1, 4).Select(i => new ReceiptPreviewLine
            {
                Name = $"Sample Item {i}",
                Sku = $"SKU{i:000}",
                Qty = (i % 3) + 1,
                Unit = 99.00m + i * 25m,
                LineDiscount = (i == 1) ? 5m : 0m
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

            var sale = new ReceiptPreviewSale
            {
                Subtotal = lines.Sum(l => l.Qty * l.Unit),
                InvoiceDiscount = 20m,
                Tax = 30m,
                OtherExpenses = 0m,
                Paid = 500m,
                Ts = DateTime.Now,
                OutletId = SelectedOutlet?.Id,
                OutletCode = SelectedOutlet?.Code ?? $"OUT-{(SelectedOutlet?.Id ?? 1):000}",
                CounterId = 1,
                CounterName = "Front Counter",
                InvoiceNumber = 1234,
                CashierName = ShowCashierOnReceipt ? "Cashier A" : null,
                BarcodeText = "123456789012",
                QrText = ShowQr ? "https://example.com/e-receipt/ABCDEF" : null
            };

            sale.Total = sale.Subtotal - sale.InvoiceDiscount + sale.Tax + sale.OtherExpenses;
            sale.Balance = Math.Max(0m, sale.Total - sale.Paid);

            int width = PaperWidthMm <= 58 ? 32 : 42;

            PreviewText = ReceiptPreviewBuilder.BuildText(
                width,
                ShowBusinessName ? OutletDisplayName : null,
                ShowAddress ? $"{AddressLine1}\n{AddressLine2}".Trim() : null,
                ShowContacts ? Phone : null,
                ShowBusinessNtn ? BusinessNtn : null,
                ShowLogo,
                RowShowProductName, RowShowProductSku, RowShowQty, RowShowUnitPrice, RowShowLineDiscount, RowShowLineTotal,
                TotalsShowTaxes, TotalsShowDiscounts, TotalsShowOtherExpenses, TotalsShowGrandTotal, TotalsShowPaymentRecv, TotalsShowBalance,
                ShowFooter ? CurrentLocalization?.Footer : null,
                EnableFbr, ShowFbrQr, FbrPosId,
                lines, sale,
                PrintBarcodeOnReceipt,
                ShowQr
            );
        }
        catch (Exception ex)
        {
            int width = PaperWidthMm <= 58 ? 32 : 42;
            PreviewText = $"[Preview error]\n{ex.Message}\n\n{new string('-', width)}\n";
        }

        return Task.CompletedTask;
    }

}
