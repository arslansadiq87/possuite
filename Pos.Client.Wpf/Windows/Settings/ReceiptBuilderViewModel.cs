// Pos.Client.Wpf/Windows/Settings/ReceiptBuilderViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Client.Wpf.Models;                  // CartLine
using Pos.Client.Wpf.Printing;               // ReceiptPreviewBuilder, RawPrinterHelper
using Pos.Client.Wpf.Services;               // IDialogService
using Pos.Domain.Entities;                   // ReceiptTemplate, Outlet, Sale, etc.
using Pos.Domain.Services;
using System.Windows.Threading;
using System.Security.Cryptography;
// IReceiptTemplateService, IInvoiceSettingsService, ILookupService
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine (type location)

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class ReceiptBuilderViewModel : ObservableObject
    {
        //private readonly IReceiptTemplateService _tplSvc;
        //private readonly IInvoiceSettingsLocalService _invoiceSettings;  // one-time seed from legacy invoice settings
        //private readonly ILookupService _lookup;                    // outlets
        //private readonly IDialogService _dialogs;                   // app dialog service
        //private System.Windows.Threading.DispatcherTimer? _livePreviewTimer;
        //private CancellationTokenSource _previewCts = new();

        //public ReceiptBuilderViewModel(
        //    IReceiptTemplateService tplSvc,
        //    IInvoiceSettingsLocalService invoiceSettings,
        //    ILookupService lookup,
        //    IDialogService dialogs)
        //{
        //    _tplSvc = tplSvc;
        //    _invoiceSettings = invoiceSettings;
        //    _lookup = lookup;
        //    _dialogs = dialogs;

        //    InstalledPrinters = new ObservableCollection<string>(
        //        PrinterSettings.InstalledPrinters.Cast<string>()
        //    );

        //    // Centralized reactions (replaces partial On...Changed hooks)
        //    PropertyChanged += async (s, e) =>
        //    {
        //        if (e.PropertyName == nameof(IsGlobal) || e.PropertyName == nameof(SelectedOutlet))
        //            await LoadAllTemplatesAsync();

        //        if (e.PropertyName == nameof(SelectedTabIndex))
        //            await PreviewCurrentTabAsync();
        //    };
        //}

        //private DispatcherTimer? _livePreviewTimer;
        //private string? _lastPreviewFingerprint;

        //public int[] PaperWidths { get; } = new[] { 58, 80 };
        //// ---------- Scope / Outlet ----------
        //[ObservableProperty] private bool isGlobal = true;        // Global templates when true
        //[ObservableProperty] private Outlet? selectedOutlet;      // When IsGlobal == false
        //public ObservableCollection<Outlet> Outlets { get; } = new();

        //// ---------- Tabs ----------
        //[ObservableProperty] private int selectedTabIndex = 0;    // 0=Sale,1=SaleReturn,2=Voucher,3=ZReport

        //// ---------- Installed Printers ----------
        //public ObservableCollection<string> InstalledPrinters { get; }

        //// ---------- Templates (one per tab) ----------
        //[ObservableProperty] private ReceiptTemplate saleTemplate = new();
        //[ObservableProperty] private ReceiptTemplate saleReturnTemplate = new();
        //[ObservableProperty] private ReceiptTemplate voucherTemplate = new();
        //[ObservableProperty] private ReceiptTemplate zReportTemplate = new();

        //// ---------- Live preview ----------
        //[ObservableProperty] private string previewText = "";

        // ======================================================
        // Init
        // ======================================================
        //public async Task InitAsync(CancellationToken ct = default)
        //{
        //    await LoadOutletsAsync(ct);
        //    await EnsureSeedFromInvoiceSettingsOnceAsync(ct); // migration-less soft copy for Sale
        //    await LoadAllTemplatesAsync(ct);
        //    await PreviewCurrentTabAsync();
        //    StartLivePreview(); // <- add this

        //}

        //private async Task LoadOutletsAsync(CancellationToken ct = default)
        //{
        //    Outlets.Clear();
        //    var all = await _lookup.GetOutletsAsync(ct);
        //    foreach (var o in all) Outlets.Add(o);

        //    // default: Global on first open
        //    if (!Outlets.Any()) IsGlobal = true;
        //}

        ///// <summary>
        ///// If there is no Sale template for the current scope, copy receipt layout from legacy InvoiceSettings once.
        ///// </summary>
        //private async Task EnsureSeedFromInvoiceSettingsOnceAsync(CancellationToken ct)
        //{
        //    int? outletId = IsGlobal ? null : SelectedOutlet?.Id;

        //    var existing = await _tplSvc.GetAsync(outletId, ReceiptDocType.Sale, ct);
        //    if (existing.Id != 0) return; // already seeded/created
        //    var counterId = AppState.Current.CurrentCounterId; // or pass the known counter
        //    var settings = await _invoiceSettings.GetForCounterWithFallbackAsync(counterId, ct);

        //    //var (settings, _) = await _invoiceSettings.GetAsync(outletId, "en", ct);
        //    var seed = new ReceiptTemplate
        //    {
                //OutletId = outletId,
                //DocType = ReceiptDocType.Sale,
                //PrinterName = settings.PrinterName,
                //PaperWidthMm = settings.PaperWidthMm <= 0 ? 80 : settings.PaperWidthMm,
                //EnableDrawerKick = settings.EnableDrawerKick,

                //OutletDisplayName = settings.OutletDisplayName,
                //AddressLine1 = settings.AddressLine1,
                //AddressLine2 = settings.AddressLine2,
                //Phone = settings.Phone,

                //LogoPng = settings.LogoPng,
                //LogoMaxWidthPx = settings.LogoMaxWidthPx,
                //LogoAlignment = string.IsNullOrWhiteSpace(settings.LogoAlignment) ? "Center" : settings.LogoAlignment,

                //ShowQr = settings.ShowQr,
                //ShowCustomerOnReceipt = settings.ShowCustomerOnReceipt,
                //ShowCashierOnReceipt = settings.ShowCashierOnReceipt,
                //PrintBarcodeOnReceipt = settings.PrintBarcodeOnReceipt,

                //RowShowProductName = settings.RowShowProductName,
                //RowShowProductSku = settings.RowShowProductSku,
                //RowShowQty = settings.RowShowQty,
                //RowShowUnitPrice = settings.RowShowUnitPrice,
                //RowShowLineDiscount = settings.RowShowLineDiscount,
                //RowShowLineTotal = settings.RowShowLineTotal,

                //TotalsShowTaxes = settings.TotalsShowTaxes,
                //TotalsShowDiscounts = settings.TotalsShowDiscounts,
                //TotalsShowOtherExpenses = settings.TotalsShowOtherExpenses,
                //TotalsShowGrandTotal = settings.TotalsShowGrandTotal,
                //TotalsShowPaymentRecv = settings.TotalsShowPaymentRecv,
                //TotalsShowBalance = settings.TotalsShowBalance
        //    };
        //    await _tplSvc.SaveAsync(seed, ct);
        //}

        //private async Task LoadAllTemplatesAsync(CancellationToken ct = default)
        //{
        //    int? outletId = IsGlobal ? null : SelectedOutlet?.Id;

        //    SaleTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Sale, ct);
        //    SaleReturnTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.SaleReturn, ct);
        //    VoucherTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Voucher, ct);
        //    ZReportTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.ZReport, ct);
        //}

        // ======================================================
        // Preview helpers
        // ======================================================
        //private async Task PreviewCurrentTabAsync()
        //{
        //    switch (SelectedTabIndex)
        //    {
        //        case 0: await BuildSalePreviewAsync(SaleTemplate); break;
        //        case 1: await BuildSaleReturnPreviewAsync(SaleReturnTemplate); break;
        //        case 2: PreviewText = BuildVoucherPreview(VoucherTemplate); break;
        //        case 3: PreviewText = BuildZReportPreview(ZReportTemplate); break;
        //    }
        //    await Task.CompletedTask;
        //}

        //private static IReadOnlyList<ReceiptPreviewLine> SampleLines =>
        //    new[]
        //    {
        //        new ReceiptPreviewLine { Name = "Milk 1L", Sku = "MILK-1L", Qty = 1, Unit = 220, LineDiscount = 0 },
        //        new ReceiptPreviewLine { Name = "Bread",   Sku = "BRD-01",  Qty = 2, Unit =  90, LineDiscount = 10 }
        //    };

        //private async Task<(string? name, string? address, string? phone, string? ntn, byte[]? logo, int logoMaxPx, string align)>
    //LoadIdentityAsync(CancellationToken ct = default)
    //    {
    //        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;
    //        var counterId = AppState.Current.CurrentCounterId; // or pass the known counter
    //        var settings = await _invoiceSettings.GetForCounterWithFallbackAsync(counterId, ct);

    //        //var (settings, _) = await _invoiceSettings.GetAsync(outletId, "en", ct);

    //        var address = string.Join("\n", new[] { settings.AddressLine1, settings.AddressLine2 }.Where(s => !string.IsNullOrWhiteSpace(s)));
    //        return (
    //            settings.OutletDisplayName,
    //            string.IsNullOrWhiteSpace(address) ? null : address,
    //            settings.Phone,
    //            settings.ShowBusinessNtn ? settings.BusinessNtn : null,
    //            settings.LogoPng,
    //            settings.LogoMaxWidthPx <= 0 ? 384 : settings.LogoMaxWidthPx,
    //            string.IsNullOrWhiteSpace(settings.LogoAlignment) ? "Center" : settings.LogoAlignment
    //        );
    //    }


        //private static ReceiptPreviewSale SampleSale(ReceiptTemplate tpl, bool isReturn = false)
        //{
        //    var gross = SampleLines.Sum(x => x.Qty * x.Unit) - SampleLines.Sum(x => x.LineDiscount);

        //    // Demo values so toggles are visible in preview
        //    var disc = Math.Round(gross * 0.05m, 2);                   // 5% invoice discount
        //    var tax = Math.Round((gross - disc) * 0.17m, 2);          // 17% tax on net
        //    var other = 50m;                                            // other expenses
        //    var total = (gross - disc) + tax + other;
        //    if (isReturn) total = -total;

        //    var paid = Math.Max(0m, total - 200m);                     // leave a balance to show
        //    var bal = Math.Max(0m, total - paid);


        //    return new ReceiptPreviewSale
        //    {
        //        Ts = DateTime.Now,
        //        OutletId = tpl.OutletId,
        //        OutletCode = tpl.OutletId?.ToString(),
        //        CounterId = 1,
        //        CounterName = "Counter-1",
        //        InvoiceNumber = 12345,
        //        CashierName = "Ali",
        //        CustomerName = "Walk-in",
        //        Subtotal = gross,
        //        InvoiceDiscount = disc,
        //        Tax = tax,
        //        OtherExpenses = other,
        //        Total = total,
        //        Paid = paid,
        //        Balance = bal,

        //        BarcodeText = "INV-12345",
        //        QrText = "https://example.com/r/INV-12345"
        //    };
        //}

        // at top if missing

        // new async instance method
        //private async Task BuildSalePreviewAsync(ReceiptTemplate tpl, CancellationToken ct = default)
        //{
        //    (string? name, string? addr, string? phone, string? ntn,
        //     byte[]? _logoPng, int _logoMax, string _logoAlign) = await LoadIdentityAsync(ct);

        //    int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
        //    var sale = SampleSale(tpl, isReturn: false);

        //    PreviewText = ReceiptPreviewBuilder.BuildText(
        //        width,
        //        name, addr, phone, ntn,
        //        showLogo: tpl.ShowLogoOnReceipt,
        //        showCustomer: tpl.ShowCustomerOnReceipt,
        //        showCashier: tpl.ShowCashierOnReceipt,
        //        // item flags
        //        tpl.RowShowProductName, tpl.RowShowProductSku, tpl.RowShowQty,
        //        tpl.RowShowUnitPrice, tpl.RowShowLineDiscount, tpl.RowShowLineTotal,
        //        // totals flags
        //        tpl.TotalsShowTaxes, tpl.TotalsShowDiscounts, tpl.TotalsShowOtherExpenses,
        //        tpl.TotalsShowGrandTotal, tpl.TotalsShowPaymentRecv, tpl.TotalsShowBalance,
        //        // footer
        //        tpl.FooterText,
        //        // FBR (off for preview)
        //        enableFbr: false, showFbrQr: false, fbrPosId: null,
        //        // data
        //        SampleLines, sale,
        //        // barcode / QR
        //        tpl.PrintBarcodeOnReceipt, tpl.ShowQr
        //    );
        //}




        //private async Task BuildSaleReturnPreviewAsync(ReceiptTemplate tpl, CancellationToken ct = default)
        //{
        //    (string? name, string? addr, string? phone, string? ntn,
        //     byte[]? _logoPng, int _logoMax, string _logoAlign) = await LoadIdentityAsync(ct);

        //    int width = tpl.PaperWidthMm <= 58 ? 32 : 42;

        //    // Build a sample **return** (note: isReturn = true)
        //    var sale = SampleSale(tpl, isReturn: true);

        //    PreviewText = ReceiptPreviewBuilder.BuildText(
        //        width,
        //        name, addr, phone, ntn,
        //        showLogo: tpl.ShowLogoOnReceipt,
        //        showCustomer: tpl.ShowCustomerOnReceipt,
        //        showCashier: tpl.ShowCashierOnReceipt,
        //        // item flags
        //        tpl.RowShowProductName, tpl.RowShowProductSku, tpl.RowShowQty,
        //        tpl.RowShowUnitPrice, tpl.RowShowLineDiscount, tpl.RowShowLineTotal,
        //        // totals flags
        //        tpl.TotalsShowTaxes, tpl.TotalsShowDiscounts, tpl.TotalsShowOtherExpenses,
        //        tpl.TotalsShowGrandTotal, tpl.TotalsShowPaymentRecv, tpl.TotalsShowBalance,
        //        // footer
        //        tpl.FooterText,
        //        // FBR (off in preview)
        //        enableFbr: false, showFbrQr: false, fbrPosId: null,
        //        // data
        //        SampleLines, sale,
        //        // barcode / QR
        //        tpl.PrintBarcodeOnReceipt, tpl.ShowQr
        //    );
        //}


        //private static string BuildVoucherPreview(ReceiptTemplate tpl)
        //{
        //    var sb = new StringBuilder();
        //    int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
        //    string line = new string('-', width);
        //    string title = Center("VOUCHER", width);

        //    sb.AppendLine(title);
        //    if (!string.IsNullOrWhiteSpace(tpl.OutletDisplayName))
        //        sb.AppendLine(Center(tpl.OutletDisplayName!.ToUpperInvariant(), width));
        //    sb.AppendLine(line);
        //    sb.AppendLine($"No: VCH-1001");
        //    sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
        //    sb.AppendLine($"Party: Walk-in");
        //    sb.AppendLine(line);
        //    sb.AppendLine($"Expense A".PadRight(width - 8) + "500.00");
        //    sb.AppendLine($"Expense B".PadRight(width - 8) + "250.00");
        //    sb.AppendLine(line);
        //    sb.AppendLine("Total".PadRight(width - 8) + "750.00");
        //    sb.AppendLine(line);
        //    if (!string.IsNullOrWhiteSpace(tpl.FooterText))
        //        sb.AppendLine(tpl.FooterText!);
        //    return sb.ToString();

        //    // RIGHT:
        //    static string Center(string s, int w) =>
        //        s.Length >= w ? s : new string(' ', Math.Max(0, (w - s.Length) / 2)) + s;
        //}

        private static string BuildZReportPreview(ReceiptTemplate tpl)
        {
            int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
            string line = new string('=', width);
            var sb = new StringBuilder();
            sb.AppendLine(Center("Z REPORT — TILL CLOSE", width));
            if (!string.IsNullOrWhiteSpace(tpl.OutletDisplayName))
                sb.AppendLine(Center(tpl.OutletDisplayName!.ToUpperInvariant(), width));
            sb.AppendLine(line);
            sb.AppendLine($"Session: 27");
            sb.AppendLine($"Opened:  {DateTime.Now.AddHours(-6):yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Closed:  {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine(new string('-', width));
            sb.AppendLine($"Opening Float".PadRight(width - 10) + "1000.00");
            sb.AppendLine($"Sales Total".PadRight(width - 10) + "15450.00");
            sb.AppendLine($"Returns Abs".PadRight(width - 10) + "  950.00");
            sb.AppendLine($"Net Total".PadRight(width - 10) + "14500.00");
            sb.AppendLine($"Cash Counted".PadRight(width - 10) + "15500.00");
            sb.AppendLine($"Over/(Short)".PadRight(width - 10) + " 1000.00");
            sb.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(tpl.FooterText))
                sb.AppendLine(tpl.FooterText!);
            return sb.ToString();

            // RIGHT:
            static string Center(string s, int w) =>
                s.Length >= w ? s : new string(' ', Math.Max(0, (w - s.Length) / 2)) + s;
        }

        // ======================================================
        // Commands
        // ======================================================

        //[RelayCommand]
        //private async Task SaveSaleAsync()
        //{
        //    await _tplSvc.SaveAsync(SaleTemplate);
        //    await _dialogs.AlertAsync("Sale receipt saved.", "Receipt Builder");
        //}

        //[RelayCommand]
        //private async Task SaveSaleReturnAsync()
        //{
        //    await _tplSvc.SaveAsync(SaleReturnTemplate);
        //    await _dialogs.AlertAsync("Sale Return receipt saved.", "Receipt Builder");
        //}

        //[RelayCommand]
        //private async Task SaveVoucherAsync()
        //{
        //    await _tplSvc.SaveAsync(VoucherTemplate);
        //    await _dialogs.AlertAsync("Voucher receipt saved.", "Receipt Builder");
        //}

        //[RelayCommand]
        //private async Task SaveZReportAsync()
        //{
        //    await _tplSvc.SaveAsync(ZReportTemplate);
        //    await _dialogs.AlertAsync("Z-Report receipt saved.", "Receipt Builder");
        //}

        //[RelayCommand]
    //    private async Task PreviewSaleAsync()
    //    {
    //        await BuildSalePreviewAsync(SaleTemplate, _previewCts.Token);
    //    }
    //    [RelayCommand]
    //    private async Task PreviewSaleReturnAsync()
    //    {
    //        await BuildSaleReturnPreviewAsync(SaleReturnTemplate, _previewCts.Token);
    //    }
    //    [RelayCommand] private Task PreviewVoucherAsync() { PreviewText = BuildVoucherPreview(VoucherTemplate); return Task.CompletedTask; }
    //    [RelayCommand] private Task PreviewZReportAsync() { PreviewText = BuildZReportPreview(ZReportTemplate); return Task.CompletedTask; }

    //    [RelayCommand]
    //    private async Task PrintSaleAsync()
    //    {
    //        // Build a synthetic Sale + Cart for test print (user previewing in builder)
    //        var sale = new Sale { Id = 0, InvoiceNumber = 9999, InvoiceFooter = SaleTemplate.FooterText };
    //        var cart = new List<CartLine>
    //{
    //    new CartLine { DisplayName = "Milk 1L", Sku = "MILK-1L", Qty = 1, UnitNet = 220m },
    //    new CartLine { DisplayName = "Bread",   Sku = "BRD-01",  Qty = 2, UnitNet =  90m }
    //};

    //        await ReceiptPrinter.PrintAsync(
    //            docType: ReceiptDocType.Sale,
    //            tpl: SaleTemplate,
    //            sale: sale,
    //            cart: cart,
    //            till: null,
    //            storeNameOverride: string.IsNullOrWhiteSpace(SaleTemplate.OutletDisplayName)
    //                ? ReceiptPrinter.DefaultStoreName
    //                : SaleTemplate.OutletDisplayName,
    //            cashierName: "Test Cashier",
    //            salesmanName: null
    //        );
    //    }

    //    private async Task<string> RenderSaleReturnTextAsync(ReceiptTemplate tpl, CancellationToken ct = default)
    //    {
    //        (string? name, string? addr, string? phone, string? ntn,
    //         byte[]? _logoPng, int _logoMax, string _logoAlign) = await LoadIdentityAsync(ct);

    //        int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
    //        var sale = SampleSale(tpl, isReturn: true);

    //        return ReceiptPreviewBuilder.BuildText(
    //            width,
    //            name, addr, phone, ntn,
    //            showLogo: tpl.ShowLogoOnReceipt,
    //            showCustomer: tpl.ShowCustomerOnReceipt,
    //            showCashier: tpl.ShowCashierOnReceipt,
    //            // item flags
    //            tpl.RowShowProductName, tpl.RowShowProductSku, tpl.RowShowQty,
    //            tpl.RowShowUnitPrice, tpl.RowShowLineDiscount, tpl.RowShowLineTotal,
    //            // totals flags
    //            tpl.TotalsShowTaxes, tpl.TotalsShowDiscounts, tpl.TotalsShowOtherExpenses,
    //            tpl.TotalsShowGrandTotal, tpl.TotalsShowPaymentRecv, tpl.TotalsShowBalance,
    //            // footer
    //            tpl.FooterText,
    //            // FBR (off)
    //            enableFbr: false, showFbrQr: false, fbrPosId: null,
    //            // data
    //            SampleLines, sale,
    //            tpl.PrintBarcodeOnReceipt, tpl.ShowQr
    //        );
    //    }



        //[RelayCommand]
        //private async Task PrintSaleReturnAsync()
        //{
        //    // For now print as text; switch to dedicated ESC/POS builder when ready
        //    var text = await RenderSaleReturnTextAsync(SaleReturnTemplate);
        //    await PrintPlainAsync(text, SaleReturnTemplate.PrinterName);
        //}

        //[RelayCommand]
        //private async Task PrintVoucherAsync()
        //{
        //    var text = BuildVoucherPreview(VoucherTemplate);
        //    await PrintPlainAsync(text, VoucherTemplate.PrinterName);
        //}

        //[RelayCommand]
        //private async Task PrintZReportAsync()
        //{
        //    var text = BuildZReportPreview(ZReportTemplate);
        //    await PrintPlainAsync(text, ZReportTemplate.PrinterName);
        //}

        //private static Task PrintPlainAsync(string text, string? printerName)
        //{
        //    var bytes = new List<byte>();
        //    bytes.AddRange(Encoding.ASCII.GetBytes(text + "\n\n"));
        //    bytes.AddRange(new byte[] { 0x1D, 0x56, 0x00 }); // GS V 0 = Cut
        //    RawPrinterHelper.SendBytesToPrinter(printerName ?? ReceiptPrinter.DefaultPrinterName, bytes.ToArray());
        //    return Task.CompletedTask;
        //}

        //private void StartLivePreview()
        //{
        //    if (_livePreviewTimer != null) return;
        //    _livePreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        //    _livePreviewTimer.Tick += async (_, __) =>
        //    {
        //        var tpl = SelectedTabIndex switch
        //        {
        //            0 => SaleTemplate,
        //            1 => SaleReturnTemplate,
        //            2 => VoucherTemplate,
        //            3 => ZReportTemplate,
        //            _ => SaleTemplate
        //        };
        //        var fp = ComputeTplFingerprint(tpl);
        //        if (fp == _lastPreviewFingerprint) return;
        //        _lastPreviewFingerprint = fp;

        //        _previewCts.Cancel();
        //        _previewCts.Dispose();
        //        _previewCts = new CancellationTokenSource();

        //        try
        //        {
        //            switch (SelectedTabIndex)
        //            {
        //                case 0: await BuildSalePreviewAsync(SaleTemplate, _previewCts.Token); break;
        //                case 1: await BuildSaleReturnPreviewAsync(SaleReturnTemplate, _previewCts.Token); break;
        //                case 2: PreviewText = BuildVoucherPreview(VoucherTemplate); break;
        //                case 3: PreviewText = BuildZReportPreview(ZReportTemplate); break;
        //            }
        //        }
        //        catch (OperationCanceledException) { }
        //    };
        //    _livePreviewTimer.Start();
        //}

        //private static string ComputeTplFingerprint(ReceiptTemplate t)
        //{
        //    // Concatenate only fields that affect preview; keep it cheap
        //    var s =
        //        $"{t.PrinterName}|{t.PaperWidthMm}|{t.EnableDrawerKick}|" +
        //        $"{t.OutletDisplayName}|{t.AddressLine1}|{t.AddressLine2}|{t.Phone}|" +
        //        $"{t.LogoMaxWidthPx}|{t.LogoAlignment}|len:{t.LogoPng?.Length ?? 0}|" +
        //        $"{t.RowShowProductName}|{t.RowShowProductSku}|{t.RowShowQty}|{t.RowShowUnitPrice}|{t.RowShowLineDiscount}|{t.RowShowLineTotal}|" +
        //        $"{t.TotalsShowTaxes}|{t.TotalsShowDiscounts}|{t.TotalsShowOtherExpenses}|{t.TotalsShowGrandTotal}|{t.TotalsShowPaymentRecv}|{t.TotalsShowBalance}|" +
        //        $"{t.ShowQr}|{t.ShowCustomerOnReceipt}|{t.ShowCashierOnReceipt}|{t.PrintBarcodeOnReceipt}|" +
        //        $"{t.HeaderText}|{t.FooterText}";
        //    using var sha = SHA256.Create();
        //    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        //    return Convert.ToHexString(hash, 0, 8);
        //}

    }
}
