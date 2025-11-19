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
using Pos.Domain.Services;                   // IReceiptTemplateService, IInvoiceSettingsService, ILookupService
using static Pos.Client.Wpf.Windows.Sales.SaleInvoiceView; // CartLine (type location)

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class ReceiptBuilderViewModel : ObservableObject
    {
        private readonly IReceiptTemplateService _tplSvc;
        private readonly IInvoiceSettingsService _invoiceSettings;  // one-time seed from legacy invoice settings
        private readonly ILookupService _lookup;                    // outlets
        private readonly IDialogService _dialogs;                   // app dialog service

        public ReceiptBuilderViewModel(
            IReceiptTemplateService tplSvc,
            IInvoiceSettingsService invoiceSettings,
            ILookupService lookup,
            IDialogService dialogs)
        {
            _tplSvc = tplSvc;
            _invoiceSettings = invoiceSettings;
            _lookup = lookup;
            _dialogs = dialogs;

            InstalledPrinters = new ObservableCollection<string>(
                PrinterSettings.InstalledPrinters.Cast<string>()
            );

            // Centralized reactions (replaces partial On...Changed hooks)
            PropertyChanged += async (s, e) =>
            {
                if (e.PropertyName == nameof(IsGlobal) || e.PropertyName == nameof(SelectedOutlet))
                    await LoadAllTemplatesAsync();

                if (e.PropertyName == nameof(SelectedTabIndex))
                    await PreviewCurrentTabAsync();
            };
        }

        // ---------- Scope / Outlet ----------
        [ObservableProperty] private bool isGlobal = true;        // Global templates when true
        [ObservableProperty] private Outlet? selectedOutlet;      // When IsGlobal == false
        public ObservableCollection<Outlet> Outlets { get; } = new();

        // ---------- Tabs ----------
        [ObservableProperty] private int selectedTabIndex = 0;    // 0=Sale,1=SaleReturn,2=Voucher,3=ZReport

        // ---------- Installed Printers ----------
        public ObservableCollection<string> InstalledPrinters { get; }

        // ---------- Templates (one per tab) ----------
        [ObservableProperty] private ReceiptTemplate saleTemplate = new();
        [ObservableProperty] private ReceiptTemplate saleReturnTemplate = new();
        [ObservableProperty] private ReceiptTemplate voucherTemplate = new();
        [ObservableProperty] private ReceiptTemplate zReportTemplate = new();

        // ---------- Live preview ----------
        [ObservableProperty] private string previewText = "";

        // ======================================================
        // Init
        // ======================================================
        public async Task InitAsync(CancellationToken ct = default)
        {
            await LoadOutletsAsync(ct);
            await EnsureSeedFromInvoiceSettingsOnceAsync(ct); // migration-less soft copy for Sale
            await LoadAllTemplatesAsync(ct);
            await PreviewCurrentTabAsync();
        }

        private async Task LoadOutletsAsync(CancellationToken ct = default)
        {
            Outlets.Clear();
            var all = await _lookup.GetOutletsAsync(ct);
            foreach (var o in all) Outlets.Add(o);

            // default: Global on first open
            if (!Outlets.Any()) IsGlobal = true;
        }

        /// <summary>
        /// If there is no Sale template for the current scope, copy receipt layout from legacy InvoiceSettings once.
        /// </summary>
        private async Task EnsureSeedFromInvoiceSettingsOnceAsync(CancellationToken ct)
        {
            int? outletId = IsGlobal ? null : SelectedOutlet?.Id;

            var existing = await _tplSvc.GetAsync(outletId, ReceiptDocType.Sale, ct);
            if (existing.Id != 0) return; // already seeded/created

            var (settings, _) = await _invoiceSettings.GetAsync(outletId, "en", ct);
            var seed = new ReceiptTemplate
            {
                OutletId = outletId,
                DocType = ReceiptDocType.Sale,
                PrinterName = settings.PrinterName,
                PaperWidthMm = settings.PaperWidthMm <= 0 ? 80 : settings.PaperWidthMm,
                EnableDrawerKick = settings.EnableDrawerKick,

                OutletDisplayName = settings.OutletDisplayName,
                AddressLine1 = settings.AddressLine1,
                AddressLine2 = settings.AddressLine2,
                Phone = settings.Phone,

                LogoPng = settings.LogoPng,
                LogoMaxWidthPx = settings.LogoMaxWidthPx,
                LogoAlignment = string.IsNullOrWhiteSpace(settings.LogoAlignment) ? "Center" : settings.LogoAlignment,

                ShowQr = settings.ShowQr,
                ShowCustomerOnReceipt = settings.ShowCustomerOnReceipt,
                ShowCashierOnReceipt = settings.ShowCashierOnReceipt,
                PrintBarcodeOnReceipt = settings.PrintBarcodeOnReceipt,

                RowShowProductName = settings.RowShowProductName,
                RowShowProductSku = settings.RowShowProductSku,
                RowShowQty = settings.RowShowQty,
                RowShowUnitPrice = settings.RowShowUnitPrice,
                RowShowLineDiscount = settings.RowShowLineDiscount,
                RowShowLineTotal = settings.RowShowLineTotal,

                TotalsShowTaxes = settings.TotalsShowTaxes,
                TotalsShowDiscounts = settings.TotalsShowDiscounts,
                TotalsShowOtherExpenses = settings.TotalsShowOtherExpenses,
                TotalsShowGrandTotal = settings.TotalsShowGrandTotal,
                TotalsShowPaymentRecv = settings.TotalsShowPaymentRecv,
                TotalsShowBalance = settings.TotalsShowBalance
            };
            await _tplSvc.SaveAsync(seed, ct);
        }

        private async Task LoadAllTemplatesAsync(CancellationToken ct = default)
        {
            int? outletId = IsGlobal ? null : SelectedOutlet?.Id;

            SaleTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Sale, ct);
            SaleReturnTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.SaleReturn, ct);
            VoucherTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Voucher, ct);
            ZReportTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.ZReport, ct);
        }

        // ======================================================
        // Preview helpers
        // ======================================================
        private async Task PreviewCurrentTabAsync()
        {
            switch (SelectedTabIndex)
            {
                case 0: PreviewText = BuildSalePreview(SaleTemplate); break;
                case 1: PreviewText = BuildSaleReturnPreview(SaleReturnTemplate); break;
                case 2: PreviewText = BuildVoucherPreview(VoucherTemplate); break;
                case 3: PreviewText = BuildZReportPreview(ZReportTemplate); break;
            }
            await Task.CompletedTask;
        }

        private static IReadOnlyList<ReceiptPreviewLine> SampleLines =>
            new[]
            {
                new ReceiptPreviewLine { Name = "Milk 1L", Sku = "MILK-1L", Qty = 1, Unit = 220, LineDiscount = 0 },
                new ReceiptPreviewLine { Name = "Bread",   Sku = "BRD-01",  Qty = 2, Unit =  90, LineDiscount = 10 }
            };

        private static ReceiptPreviewSale SampleSale(ReceiptTemplate tpl, bool isReturn = false)
        {
            var gross = SampleLines.Sum(x => x.Qty * x.Unit) - SampleLines.Sum(x => x.LineDiscount);
            var disc = 0m;
            var tax = Math.Round(gross * 0.0m, 2);
            var other = 0m;
            var total = isReturn ? -gross : gross;
            var paid = total;
            var bal = 0m;

            return new ReceiptPreviewSale
            {
                Ts = DateTime.Now,
                OutletId = tpl.OutletId,
                OutletCode = tpl.OutletId?.ToString(),
                CounterId = 1,
                CounterName = "Counter-1",
                InvoiceNumber = 12345,
                CashierName = "Ali",

                Subtotal = gross,
                InvoiceDiscount = disc,
                Tax = tax,
                OtherExpenses = other,
                Total = total,
                Paid = paid,
                Balance = bal,

                BarcodeText = "INV-12345",
                QrText = "https://example.com/r/INV-12345"
            };
        }

        private static string BuildSalePreview(ReceiptTemplate tpl)
        {
            int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
            var address = string.Join("\n", new[] { tpl.AddressLine1, tpl.AddressLine2 }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var contacts = string.IsNullOrWhiteSpace(tpl.Phone) ? null : $"Ph: {tpl.Phone}";
            var sale = SampleSale(tpl, isReturn: false);

            return ReceiptPreviewBuilder.BuildText(
                width,
                tpl.OutletDisplayName,
                address,
                contacts,
                null,                            // businessNTN (kept in InvoiceSettings if you want)
                showLogo: tpl.LogoPng != null,   // preview shows [LOGO]
                                                 // item flags
                tpl.RowShowProductName, tpl.RowShowProductSku, tpl.RowShowQty, tpl.RowShowUnitPrice, tpl.RowShowLineDiscount, tpl.RowShowLineTotal,
                // totals flags
                tpl.TotalsShowTaxes, tpl.TotalsShowDiscounts, tpl.TotalsShowOtherExpenses, tpl.TotalsShowGrandTotal, tpl.TotalsShowPaymentRecv, tpl.TotalsShowBalance,
                // footer
                tpl.FooterText,
                // FBR flags (not previewed here)
                enableFbr: false, showFbrQr: false, fbrPosId: null,
                // data
                SampleLines, sale,
                // barcode & QR toggles
                tpl.PrintBarcodeOnReceipt, tpl.ShowQr
            );
        }

        private static string BuildSaleReturnPreview(ReceiptTemplate tpl)
        {
            // Simple variant for now; when you add a dedicated return builder, swap preview too.
            return BuildSalePreview(tpl).Replace("SALE", "SALE RETURN");
        }

        private static string BuildVoucherPreview(ReceiptTemplate tpl)
        {
            var sb = new StringBuilder();
            int width = tpl.PaperWidthMm <= 58 ? 32 : 42;
            string line = new string('-', width);
            string title = Center("VOUCHER", width);

            sb.AppendLine(title);
            if (!string.IsNullOrWhiteSpace(tpl.OutletDisplayName))
                sb.AppendLine(Center(tpl.OutletDisplayName!.ToUpperInvariant(), width));
            sb.AppendLine(line);
            sb.AppendLine($"No: VCH-1001");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Party: Walk-in");
            sb.AppendLine(line);
            sb.AppendLine($"Expense A".PadRight(width - 8) + "500.00");
            sb.AppendLine($"Expense B".PadRight(width - 8) + "250.00");
            sb.AppendLine(line);
            sb.AppendLine("Total".PadRight(width - 8) + "750.00");
            sb.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(tpl.FooterText))
                sb.AppendLine(tpl.FooterText!);
            return sb.ToString();

            // RIGHT:
            static string Center(string s, int w) =>
                s.Length >= w ? s : new string(' ', Math.Max(0, (w - s.Length) / 2)) + s;
        }

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

        [RelayCommand]
        private async Task SaveSaleAsync()
        {
            await _tplSvc.SaveAsync(SaleTemplate);
            await _dialogs.AlertAsync("Sale receipt saved.", "Receipt Builder");
        }

        [RelayCommand]
        private async Task SaveSaleReturnAsync()
        {
            await _tplSvc.SaveAsync(SaleReturnTemplate);
            await _dialogs.AlertAsync("Sale Return receipt saved.", "Receipt Builder");
        }

        [RelayCommand]
        private async Task SaveVoucherAsync()
        {
            await _tplSvc.SaveAsync(VoucherTemplate);
            await _dialogs.AlertAsync("Voucher receipt saved.", "Receipt Builder");
        }

        [RelayCommand]
        private async Task SaveZReportAsync()
        {
            await _tplSvc.SaveAsync(ZReportTemplate);
            await _dialogs.AlertAsync("Z-Report receipt saved.", "Receipt Builder");
        }

        [RelayCommand] private Task PreviewSaleAsync() { PreviewText = BuildSalePreview(SaleTemplate); return Task.CompletedTask; }
        [RelayCommand] private Task PreviewSaleReturnAsync() { PreviewText = BuildSaleReturnPreview(SaleReturnTemplate); return Task.CompletedTask; }
        [RelayCommand] private Task PreviewVoucherAsync() { PreviewText = BuildVoucherPreview(VoucherTemplate); return Task.CompletedTask; }
        [RelayCommand] private Task PreviewZReportAsync() { PreviewText = BuildZReportPreview(ZReportTemplate); return Task.CompletedTask; }

        [RelayCommand]
        private async Task PrintSaleAsync()
        {
            // Build a synthetic Sale + Cart for test print (user previewing in builder)
            var sale = new Sale { Id = 0, InvoiceNumber = 9999, InvoiceFooter = SaleTemplate.FooterText };
            var cart = new List<CartLine>
    {
        new CartLine { DisplayName = "Milk 1L", Sku = "MILK-1L", Qty = 1, UnitNet = 220m },
        new CartLine { DisplayName = "Bread",   Sku = "BRD-01",  Qty = 2, UnitNet =  90m }
    };

            await ReceiptPrinter.PrintAsync(
                docType: ReceiptDocType.Sale,
                tpl: SaleTemplate,
                sale: sale,
                cart: cart,
                till: null,
                storeNameOverride: string.IsNullOrWhiteSpace(SaleTemplate.OutletDisplayName)
                    ? ReceiptPrinter.DefaultStoreName
                    : SaleTemplate.OutletDisplayName,
                cashierName: "Test Cashier",
                salesmanName: null
            );
        }


        [RelayCommand]
        private async Task PrintSaleReturnAsync()
        {
            // For now print as text; switch to dedicated ESC/POS builder when ready
            var text = BuildSaleReturnPreview(SaleReturnTemplate);
            await PrintPlainAsync(text, SaleReturnTemplate.PrinterName);
        }

        [RelayCommand]
        private async Task PrintVoucherAsync()
        {
            var text = BuildVoucherPreview(VoucherTemplate);
            await PrintPlainAsync(text, VoucherTemplate.PrinterName);
        }

        [RelayCommand]
        private async Task PrintZReportAsync()
        {
            var text = BuildZReportPreview(ZReportTemplate);
            await PrintPlainAsync(text, ZReportTemplate.PrinterName);
        }

        private static Task PrintPlainAsync(string text, string? printerName)
        {
            var bytes = new List<byte>();
            bytes.AddRange(Encoding.ASCII.GetBytes(text + "\n\n"));
            bytes.AddRange(new byte[] { 0x1D, 0x56, 0x00 }); // GS V 0 = Cut
            RawPrinterHelper.SendBytesToPrinter(printerName ?? ReceiptPrinter.DefaultPrinterName, bytes.ToArray());
            return Task.CompletedTask;
        }
    }
}
