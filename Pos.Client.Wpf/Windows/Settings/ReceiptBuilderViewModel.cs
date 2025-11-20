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
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Models;                    // CartLine
using Pos.Client.Wpf.Printing;                 // ReceiptPreviewBuilder, IRawPrinterService, EscPosReceiptBuilder
using Pos.Client.Wpf.Services;                 // IDialogService
using Pos.Domain.Entities;                     // ReceiptTemplate, Outlet, Sale, IdentitySettings, ReceiptDocType
using Pos.Domain.Services;                     // IReceiptTemplateService, IOutletService, IIdentitySettingsService, IInvoiceSettingsLocalService, ITerminalContext, IInvoiceSettingsScopedService
using Pos.Domain.Settings;                     // InvoiceSettingsLocal

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class ReceiptBuilderViewModel : ObservableObject
    {
        private readonly IReceiptTemplateService _tplSvc;
        private readonly IOutletService _outlets;
        private readonly IIdentitySettingsService _identitySvc;
        private readonly IInvoiceSettingsLocalService _invoiceLocalSvc;
        private readonly ITerminalContext _ctx;
        private readonly IDialogService _dialogs;
        private readonly IServiceProvider _sp;
        private readonly IInvoiceSettingsScopedService _invoiceScopedSvc;

        private CancellationTokenSource? _previewCts;

        // cached settings for preview/print
        private IdentitySettings? _identity;
        private InvoiceSettingsLocal? _invoiceLocal;
        private InvoiceSettingsScoped? _invoiceScoped;

        public ReceiptBuilderViewModel(
            IReceiptTemplateService tplSvc,
            IOutletService outlets,
            IIdentitySettingsService identitySvc,
            IInvoiceSettingsLocalService invoiceLocalSvc,
            ITerminalContext ctx,
            IDialogService dialogs,
            IServiceProvider sp,
            IInvoiceSettingsScopedService invoiceScopedSvc  // NEW
        )
        {
            _tplSvc = tplSvc;
            _outlets = outlets;
            _identitySvc = identitySvc;
            _invoiceLocalSvc = invoiceLocalSvc;
            _ctx = ctx;
            _dialogs = dialogs;
            _sp = sp;
            _invoiceScopedSvc = invoiceScopedSvc;

            InstalledPrinters = new ObservableCollection<string>(
                PrinterSettings.InstalledPrinters.Cast<string>());
        }

        // ----- UI State -----
        [ObservableProperty] private ObservableCollection<string> installedPrinters = new();
        [ObservableProperty] private ObservableCollection<Outlet> outletChoices = new();
        [ObservableProperty] private Outlet? selectedOutlet;

        // SALE (existing)
        [ObservableProperty] private ReceiptTemplate saleTemplate = new();
        [ObservableProperty] private string previewText = "";

        // Read-only display of the printer selected in Invoice Settings
        [ObservableProperty] private string? invoicePrinterName;

        // Enabled by General Settings (read-only flags for UI enable/disable)
        [ObservableProperty] private bool runtimeEnableFbr; // true if general settings enable FBR
        [ObservableProperty] private bool runtimeHasNtn;    // true if general settings have a non-empty NTN

        // Logo bytes from Identity/General (XAML uses BytesToBitmap converter)
        [ObservableProperty] private byte[]? identityLogoBytes;

        // ---------- NEW for extra tabs ----------
        [ObservableProperty] private ReceiptTemplate saleReturnTemplate = new();
        [ObservableProperty] private ReceiptTemplate voucherTemplate = new();
        [ObservableProperty] private ReceiptTemplate zReportTemplate = new();

        [ObservableProperty] private string saleReturnPreviewText = string.Empty;
        [ObservableProperty] private string voucherPreviewText = string.Empty;
        [ObservableProperty] private string zReportPreviewText = string.Empty;

        // Optional: if XAML binds to selected tab
        [ObservableProperty] private int selectedTabIndex; // 0 Sale, 1 Sale Return, 2 Voucher, 3 Z Report



        // ----- Init -----
        public async Task InitAsync(CancellationToken ct = default)
        {
            var outlets = await _outlets.GetAllAsync(ct);
            OutletChoices = new ObservableCollection<Outlet>(outlets.OrderBy(o => o.Name));

            // prefer the current terminal’s outlet
            SelectedOutlet = OutletChoices.FirstOrDefault(o => o.Id == _ctx.OutletId)
                             ?? OutletChoices.FirstOrDefault();

            await LoadRuntimeSettingsAsync(ct);
            await LoadAllTemplatesAsync(ct);

            // Build all previews (Sale uses PreviewText; others use their own)
            await BuildSalePreviewAsync(ct);
            await BuildSaleReturnPreviewAsync(ct);
            await BuildVoucherPreviewAsync(ct);
            await BuildZReportPreviewAsync(ct);
        }

        private async Task LoadAllTemplatesAsync(CancellationToken ct)
        {
            var outletId = SelectedOutlet?.Id ?? _ctx.OutletId;

            // Keep your existing dedicated loader for Sale too (safe)
            SaleTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Sale, ct);
            SaleReturnTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.SaleReturn, ct);
            VoucherTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Voucher, ct);
            ZReportTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.ZReport, ct);
        }

        // (kept for compatibility if called elsewhere)
        private async Task LoadSaleTemplateAsync(CancellationToken ct)
        {
            var outletId = SelectedOutlet?.Id ?? _ctx.OutletId;
            SaleTemplate = await _tplSvc.GetOrCreateDefaultAsync(outletId, ReceiptDocType.Sale, ct);
        }

        private async Task LoadRuntimeSettingsAsync(CancellationToken ct)
        {
            var outletId = SelectedOutlet?.Id ?? _ctx.OutletId;
            _identity = await _identitySvc.GetAsync(outletId, ct);
            _invoiceLocal = await _invoiceLocalSvc.GetForCounterWithFallbackAsync(_ctx.CounterId, ct);
            _invoiceScoped = await _invoiceScopedSvc.GetForOutletAsync(_ctx.OutletId, ct);

            InvoicePrinterName = string.IsNullOrWhiteSpace(_invoiceLocal?.PrinterName)
                ? "(not set)"
                : _invoiceLocal!.PrinterName!;

            // Identity/General logo bytes
            IdentityLogoBytes = _identity?.LogoPng; // or whatever your logo byte property is named

            // New: runtime flags from General Settings
            RuntimeEnableFbr = _identity?.EnableFbr == true;
            RuntimeHasNtn = !string.IsNullOrWhiteSpace(_identity?.BusinessNtn);
        }



        // ----- Commands (existing + new) -----

        // EXISTING: Save Sale only
        [RelayCommand]
        private async Task SaveSaleAsync()
        {
            try
            {
                SaleTemplate.DocType = ReceiptDocType.Sale;
                SaleTemplate.OutletId = SelectedOutlet?.Id ?? _ctx.OutletId;
                SaleTemplate.UpdatedAtUtc = DateTime.UtcNow;
                await _tplSvc.SaveAsync(SaleTemplate);
                await _dialogs.AlertAsync("Sale receipt saved.", "Receipt Builder");
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Save failed: " + ex.Message, "Receipt Builder");
            }
        }

        // NEW: Save all four in one go
        [RelayCommand]
        private async Task SaveAllAsync()
        {
            try
            {
                var outletId = SelectedOutlet?.Id ?? _ctx.OutletId;
                var now = DateTime.UtcNow;

                void Touch(ReceiptTemplate t, ReceiptDocType dt)
                {
                    t.DocType = dt;
                    t.OutletId = outletId;
                    t.UpdatedAtUtc = now;
                }

                Touch(SaleTemplate, ReceiptDocType.Sale);
                Touch(SaleReturnTemplate, ReceiptDocType.SaleReturn);
                Touch(VoucherTemplate, ReceiptDocType.Voucher);
                Touch(ZReportTemplate, ReceiptDocType.ZReport);

                await _tplSvc.SaveAsync(SaleTemplate);
                await _tplSvc.SaveAsync(SaleReturnTemplate);
                await _tplSvc.SaveAsync(VoucherTemplate);
                await _tplSvc.SaveAsync(ZReportTemplate);

                await _dialogs.AlertAsync("Saved templates for Sale, Sale Return, Voucher, and Z Report.", "Receipt Builder");
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Save All failed: " + ex.Message, "Receipt Builder");
            }
        }

        [RelayCommand]
        private async Task PreviewSaleAsync()
        {
            await BuildSalePreviewAsync();
        }

        // NEW: per-tab preview commands
        [RelayCommand]
        private async Task PreviewSaleReturnAsync() => await BuildSaleReturnPreviewAsync();

        [RelayCommand]
        private async Task PreviewVoucherAsync() => await BuildVoucherPreviewAsync();

        [RelayCommand]
        private async Task PreviewZReportAsync() => await BuildZReportPreviewAsync();

        // Optional: Print current tab (you can wire real print later)
        [RelayCommand]
        private async Task PrintCurrentAsync()
        {
            try
            {
                switch (SelectedTabIndex)
                {
                    case 0:
                        await PrintSaleAsync(); // your existing print stays intact
                        break;
                    case 1:
                        await _dialogs.AlertAsync("Hook PrintSaleReturnAsync here.", "Receipt Builder");
                        break;
                    case 2:
                        await _dialogs.AlertAsync("Hook PrintVoucherAsync here.", "Receipt Builder");
                        break;
                    case 3:
                        await _dialogs.AlertAsync("Hook PrintZReportAsync here.", "Receipt Builder");
                        break;
                }
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Print failed: " + ex.Message, "Receipt Builder");
            }
        }

        [RelayCommand]
        private async Task PrintSaleAsync()
        {
            try
            {
                // Ensure we have up-to-date runtime settings
                await LoadRuntimeSettingsAsync(CancellationToken.None);

                var identity = _identity ?? new IdentitySettings();
                var invoice = _invoiceLocal ?? new InvoiceSettingsLocal();
                var inoiceScoped = _invoiceScoped ?? new InvoiceSettingsScoped();

                var (sale, cart) = BuildSampleSaleAndCart(SaleTemplate, inoiceScoped);

                // EscPos builder currently uses a fixed layout – pass identity strings to mimic header,
                // but the real content/logo is handled in your ESC/POS builder as you evolve it.
                var bytes = EscPosReceiptBuilder.Build(
                    sale,
                    cart,
                    till: null,
                    storeName: identity.OutletDisplayName ?? "My Store",
                    cashierName: "Cashier",
                    salesmanName: null,
                    eReceiptBaseUrl: null
                );

                var printerName = string.IsNullOrWhiteSpace(invoice.PrinterName)
                    ? ReceiptPrinter.DefaultPrinterName
                    : invoice.PrinterName!;

                var raw = _sp.GetRequiredService<IRawPrinterService>();
                await raw.SendEscPosAsync(printerName, bytes);

                await _dialogs.AlertAsync($"Test receipt sent to “{printerName}”.", "Receipt Builder");
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Print failed: " + ex.Message, "Receipt Builder");
            }
        }



        // ----- Preview builders -----

        private async Task BuildSalePreviewAsync(CancellationToken ct = default)
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();

            // Ensure caches
            await LoadRuntimeSettingsAsync(ct);

            var identity = _identity ?? new IdentitySettings();
            var invoice = _invoiceLocal ?? new InvoiceSettingsLocal();

            int widthCols = SaleTemplate.PaperWidthMm <= 58 ? 32 : 42;

            // Identity & Branding text (NOT from template)
            string businessName = identity.OutletDisplayName ?? "";
            string address = JoinNonEmpty("\n", identity.AddressLine1, identity.AddressLine2);
            string contacts = JoinNonEmpty("  ", identity.Phone);

            // NTN show rule: if provided in general settings AND template says to show
            string? ntnToShow = (SaleTemplate.ShowNtnOnReceipt && !string.IsNullOrWhiteSpace(identity.BusinessNtn))
                ? identity.BusinessNtn!
                : null;

            // FBR show rule: if general settings enable FBR AND template says to show
            bool enableFbr = identity.EnableFbr && SaleTemplate.ShowFbrOnReceipt;
            bool showFbrQr = enableFbr;
            string? fbrPosId = enableFbr ? identity.FbrPosId : null;

            // Footer: from invoice settings (Sale); fallback string if blank
            string footer = string.IsNullOrWhiteSpace(_invoiceScoped?.FooterSale)
                ? "Thank you for shopping with us!"
                : _invoiceScoped?.FooterSale!;

            var lines = BuildSampleLines();
            var sale = BuildSampleSaleHeader(SaleTemplate);

            PreviewText = ReceiptPreviewBuilder.BuildText(
                width: widthCols,
                businessName: businessName,
                addressBlock: address,
                contacts: contacts,
                businessNtn: ntnToShow,                 // still pass explicitly if builder uses it
                showLogo: SaleTemplate.ShowLogoOnReceipt,
                showCustomer: SaleTemplate.ShowCustomerOnReceipt,
                showCashier: SaleTemplate.ShowCashierOnReceipt,
                // item flags
                showName: SaleTemplate.RowShowProductName,
                showSku: SaleTemplate.RowShowProductSku,
                showQty: SaleTemplate.RowShowQty,
                showUnit: SaleTemplate.RowShowUnitPrice,
                showLineDisc: SaleTemplate.RowShowLineDiscount,
                showLineTotal: SaleTemplate.RowShowLineTotal,
                // totals flags
                showTax: SaleTemplate.TotalsShowTaxes,
                showInvDisc: SaleTemplate.TotalsShowDiscounts,
                showOtherExp: SaleTemplate.TotalsShowOtherExpenses,
                showGrand: SaleTemplate.TotalsShowGrandTotal,
                showPaid: SaleTemplate.TotalsShowPaymentRecv,
                showBalance: SaleTemplate.TotalsShowBalance,
                // footer (from Invoice Settings)
                footer: footer,
                // FBR (from Identity + template toggle)
                enableFbr: enableFbr,
                showFbrQr: showFbrQr,
                fbrPosId: fbrPosId,
                // data
                lines: lines,
                sale: sale,
                // barcode / QR
                showBarcodeOnReceipt: SaleTemplate.PrintBarcodeOnReceipt,
                showGenericQr: SaleTemplate.ShowQr
            );
        }

        // NEW: Sale Return preview (same look, different template & footer slot if needed)
        private async Task BuildSaleReturnPreviewAsync(CancellationToken ct = default)
        {
            await LoadRuntimeSettingsAsync(ct);

            var identity = _identity ?? new IdentitySettings();
            int widthCols = SaleReturnTemplate.PaperWidthMm <= 58 ? 32 : 42;

            string businessName = identity.OutletDisplayName ?? "";
            string address = JoinNonEmpty("\n", identity.AddressLine1, identity.AddressLine2);
            string contacts = JoinNonEmpty("  ", identity.Phone);

            string? ntnToShow = (SaleReturnTemplate.ShowNtnOnReceipt && !string.IsNullOrWhiteSpace(identity.BusinessNtn))
                ? identity.BusinessNtn!
                : null;

            bool enableFbr = identity.EnableFbr && SaleReturnTemplate.ShowFbrOnReceipt;
            bool showFbrQr = enableFbr;
            string? fbrPosId = enableFbr ? identity.FbrPosId : null;

            // Footer: reuse sale footer for preview; you can create FooterSaleReturn in your scoped settings later
            string footer = string.IsNullOrWhiteSpace(_invoiceScoped?.FooterSale)
                ? "Thank you for shopping with us!"
                : _invoiceScoped?.FooterSale!;

            var lines = BuildSampleLines();
            var sale = BuildSampleSaleHeader(SaleReturnTemplate);
            sale.IsReturn = true; // mark as return if your preview builder reacts to it

            SaleReturnPreviewText = ReceiptPreviewBuilder.BuildText(
                width: widthCols,
                businessName: businessName,
                addressBlock: address,
                contacts: contacts,
                businessNtn: ntnToShow,
                showLogo: SaleReturnTemplate.ShowLogoOnReceipt,
                showCustomer: SaleReturnTemplate.ShowCustomerOnReceipt,
                showCashier: SaleReturnTemplate.ShowCashierOnReceipt,
                // items
                showName: SaleReturnTemplate.RowShowProductName,
                showSku: SaleReturnTemplate.RowShowProductSku,
                showQty: SaleReturnTemplate.RowShowQty,
                showUnit: SaleReturnTemplate.RowShowUnitPrice,
                showLineDisc: SaleReturnTemplate.RowShowLineDiscount,
                showLineTotal: SaleReturnTemplate.RowShowLineTotal,
                // totals
                showTax: SaleReturnTemplate.TotalsShowTaxes,
                showInvDisc: SaleReturnTemplate.TotalsShowDiscounts,
                showOtherExp: SaleReturnTemplate.TotalsShowOtherExpenses,
                showGrand: SaleReturnTemplate.TotalsShowGrandTotal,
                showPaid: SaleReturnTemplate.TotalsShowPaymentRecv,
                showBalance: SaleReturnTemplate.TotalsShowBalance,
                footer: footer,
                enableFbr: enableFbr,
                showFbrQr: showFbrQr,
                fbrPosId: fbrPosId,
                lines: lines,
                sale: sale,
                showBarcodeOnReceipt: SaleReturnTemplate.PrintBarcodeOnReceipt,
                showGenericQr: SaleReturnTemplate.ShowQr
            );
        }

        // NEW: Voucher preview (same preview surface)
        // ===== VOUCHER PREVIEW (account DR/CR, no product rows) =====
        private async Task BuildVoucherPreviewAsync(CancellationToken ct = default)
        {
            await LoadRuntimeSettingsAsync(ct);

            var identity = _identity ?? new IdentitySettings();
            int cols = VoucherTemplate.PaperWidthMm <= 58 ? 32 : 42;

            // Sample voucher (for preview only) — replace with live data if you wire it
            var v = BuildSampleVoucher();

            var sb = new StringBuilder();

            // Header
            sb.AppendLine(Center($"*** {v.Type.ToString().ToUpper()} VOUCHER ***", cols));
            sb.AppendLine(Line("No:", string.IsNullOrWhiteSpace(v.RefNo) ? v.Id.ToString() : v.RefNo!, cols));
            sb.AppendLine(Line("Date (UTC):", v.TsUtc.ToString("yyyy-MM-dd HH:mm"), cols));
            sb.AppendLine(Line("Status:", v.Status.ToString(), cols));
            sb.AppendLine(Line("Revision:", v.RevisionNo.ToString(), cols));
            if (!string.IsNullOrWhiteSpace(v.Memo))
                sb.AppendLine(Line("Memo:", v.Memo!, cols));

            // Identity header (same style as others, but not producty)
            var business = identity.OutletDisplayName ?? "STORE";
            var address = JoinNonEmpty(" ", identity.AddressLine1, identity.AddressLine2);
            var phone = identity.Phone;

            sb.AppendLine(new string('=', cols));
            sb.AppendLine(Center(business, cols));
            if (!string.IsNullOrWhiteSpace(address)) sb.AppendLine(Center(address, cols));
            if (!string.IsNullOrWhiteSpace(phone)) sb.AppendLine(Center(phone!, cols));
            sb.AppendLine(new string('-', cols));

            // Lines table
            sb.AppendLine(FixedColumns("Account / Description", "DR", "CR", cols));

            decimal dr = 0m, cr = 0m;
            foreach (var ln in v.Lines)
            {
                if (ln.Debit == 0m && ln.Credit == 0m) continue;
                var left = $"Acc {ln.AccountId}" + (string.IsNullOrWhiteSpace(ln.Description) ? "" : $" - {ln.Description}");
                sb.AppendLine(FixedColumns(left, ln.Debit == 0 ? "" : ln.Debit.ToString("0.00"),
                                                ln.Credit == 0 ? "" : ln.Credit.ToString("0.00"), cols));
                dr += ln.Debit;
                cr += ln.Credit;
            }

            sb.AppendLine(new string('-', cols));
            sb.AppendLine(FixedColumns("TOTAL", dr.ToString("0.00"), cr.ToString("0.00"), cols));

            var diff = dr - cr;
            var balanceTxt = diff == 0 ? "Balanced" : (diff > 0 ? $"DR>CR by {diff:0.00}" : $"CR>DR by {Math.Abs(diff):0.00}");
            sb.AppendLine(Line("Check:", balanceTxt, cols));

            sb.AppendLine(new string('-', cols));
            sb.AppendLine(Center("Thank you.", cols));

            VoucherPreviewText = sb.ToString();
        }

        // ===== Z REPORT PREVIEW (till totals, no product rows) =====
        private async Task BuildZReportPreviewAsync(CancellationToken ct = default)
        {
            await LoadRuntimeSettingsAsync(ct);

            int cols = ZReportTemplate.PaperWidthMm <= 58 ? 32 : 42;

            // Sample Z data (for preview only)
            var z = BuildSampleZ();

            var sb = new StringBuilder();
            sb.AppendLine(Center("*** Z REPORT — TILL CLOSE ***", cols));
            sb.AppendLine(Line("Session:", z.TillSessionId.ToString(), cols));
            sb.AppendLine(Line("Opened (UTC):", z.OpenedAtUtc.ToString("yyyy-MM-dd HH:mm"), cols));
            sb.AppendLine(Line("Closed (UTC):", z.ClosedAtUtc.ToString("yyyy-MM-dd HH:mm"), cols));
            sb.AppendLine(new string('-', cols));
            sb.AppendLine(Line("Opening Float:", z.OpeningFloat.ToString("0.00"), cols));
            sb.AppendLine(Line("Sales:", z.SalesTotal.ToString("0.00"), cols));
            sb.AppendLine(Line("Returns:", z.ReturnsTotalAbs.ToString("0.00"), cols));
            sb.AppendLine(Line("Net:", z.NetTotal.ToString("0.00"), cols));
            sb.AppendLine(Line("Cash Counted:", z.CashCounted.ToString("0.00"), cols));
            sb.AppendLine(Line("Over/Short:", z.OverShort.ToString("0.00"), cols));
            sb.AppendLine(new string('-', cols));
            sb.AppendLine(Center("Shift closed.", cols));

            ZReportPreviewText = sb.ToString();
        }

        // Sample voucher for preview surface (no products)
        private static Pos.Domain.Accounting.Voucher BuildSampleVoucher()
        {
            return new Pos.Domain.Accounting.Voucher
            {
                Id = 1001,
                TsUtc = DateTime.UtcNow,
                Type = Pos.Domain.Accounting.VoucherType.Payment,
                RefNo = "PV-1001",
                Memo = "Expense payout",
                Status = Pos.Domain.Accounting.VoucherStatus.Posted,
                RevisionNo = 1,
                Lines = new List<Pos.Domain.Accounting.VoucherLine>
        {
            new() { AccountId = 501, Description = "Utilities", Debit = 1500m, Credit = 0m },
            new() { AccountId = 111, Description = "Cash in Hand", Debit = 0m,    Credit = 1500m },
        }
            };
        }

        // Sample Z report totals for preview
        private static Pos.Client.Wpf.Printing.ZReportModel BuildSampleZ()
        {
            var now = DateTime.UtcNow;
            return new Pos.Client.Wpf.Printing.ZReportModel
            {
                TillSessionId = 42,
                OpenedAtUtc = now.AddHours(-8),
                ClosedAtUtc = now,
                OpeningFloat = 500m,
                SalesTotal = 2500m,
                ReturnsTotalAbs = 200m,
                CashCounted = 2800m
            };
        }

        // String helpers used by the previews
        private static string Center(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return "\n";
            if (text.Length >= width) return text[..width] + "\n";
            var pad = Math.Max(0, (width - text.Length) / 2);
            return new string(' ', pad) + text + "\n";
        }

        private static string Line(string key, string value, int width)
        {
            key ??= ""; value ??= "";
            var space = Math.Max(1, width - key.Length - value.Length);
            if (space < 1)
            {
                var maxKey = Math.Max(0, width - value.Length - 1);
                if (key.Length > maxKey) key = key[..maxKey];
                space = 1;
            }
            return key + new string(' ', space) + value + "\n";
        }

        private static string FixedColumns(string left, string dr, string cr, int width)
        {
            const int amtWidth = 8; // 123456.78
            const int gap = 1;
            int rightReserved = (amtWidth + gap) + amtWidth;
            int leftWidth = Math.Max(0, width - rightReserved - gap);
            if (left.Length > leftWidth) left = left[..leftWidth];
            string right(string s, int w) => s.Length >= w ? s[^w..] : new string(' ', w - s.Length) + s;
            return left.PadRight(leftWidth) + new string(' ', gap) + right(dr ?? "", amtWidth) + new string(' ', gap) + right(cr ?? "", amtWidth);
        }


        private static string JoinNonEmpty(string sep, params string?[] parts)
            => string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        private static IReadOnlyList<ReceiptPreviewLine> BuildSampleLines()
            => new List<ReceiptPreviewLine>
            {
                new() { Name = "Milk 1L",    Sku = "MILK-1L", Qty = 1, Unit = 220, LineDiscount = 0 },
                new() { Name = "Bread",      Sku = "BRD-01",  Qty = 2, Unit =  90, LineDiscount = 10 },
                new() { Name = "Eggs (12)",  Sku = "EGG-12",  Qty = 1, Unit = 380, LineDiscount = 0 },
            };

        private static ReceiptPreviewSale BuildSampleSaleHeader(ReceiptTemplate tpl)
        {
            // Compute a simple demo summary that aligns with the lines above
            decimal sub = 220m + (2 * 90m - 10m) + 380m; // 770
            decimal tax = tpl.TotalsShowTaxes ? Math.Round(sub * 0.05m, 2) : 0m;
            decimal invDisc = tpl.TotalsShowDiscounts ? 20m : 0m;
            decimal other = tpl.TotalsShowOtherExpenses ? 0m : 0m;
            decimal grand = sub + tax - invDisc + other;
            decimal paid = tpl.TotalsShowPaymentRecv ? grand : 0m;
            decimal balance = grand - paid;

            return new ReceiptPreviewSale
            {
                Ts = DateTime.Now,
                OutletId = tpl.OutletId,
                OutletCode = "MAIN",
                CounterId = 1,
                CounterName = "Counter 1",
                InvoiceNumber = 12345,
                CashierName = tpl.ShowCashierOnReceipt ? "Alice" : null,
                CustomerName = tpl.ShowCustomerOnReceipt ? "Walk-in Customer" : null,

                // Totals expected by ReceiptPreviewBuilder
                Paid = paid,
                InvoiceDiscount = invDisc,
                Total = grand,              // builder uses 'Total' for grand total line
                Tax = tax,
                OtherExpenses = other,
                Balance = balance,

                BarcodeText = tpl.PrintBarcodeOnReceipt ? "INV-12345" : null,
                QrText = tpl.ShowQr ? "https://example.com/e/INV-12345" : null,

                IsReturn = false
            };
        }

        private static (Sale sale, List<CartLine> cart) BuildSampleSaleAndCart(ReceiptTemplate tpl, InvoiceSettingsScoped invoiceScoped)
        {
            var sale = new Sale
            {
                Id = 0,
                InvoiceNumber = 12345,
                // Footer comes from invoice settings, not template
                InvoiceFooter = string.IsNullOrWhiteSpace(invoiceScoped?.FooterSale)
                                ? "Thank you for shopping with us!"
                                : invoiceScoped?.FooterSale!,
                IsReturn = false
            };

            var cart = new List<CartLine>
            {
                new CartLine { DisplayName = "Milk 1L",    Sku = "MILK-1L", Qty = 1, UnitPrice = 220m, LineTotal = 220m },
                new CartLine { DisplayName = "Bread",      Sku = "BRD-01",  Qty = 2, UnitPrice =  90m, LineTotal = 170m },
                new CartLine { DisplayName = "Eggs (12)",  Sku = "EGG-12",  Qty = 1, UnitPrice = 380m, LineTotal = 380m },
            };
            return (sale, cart);
        }

        // React to outlet switching (your signature uses oldValue, newValue)
        partial void OnSelectedOutletChanged(Outlet? oldValue, Outlet? newValue)
        {
            _ = RefreshForOutletAsync();
        }

        private async Task RefreshForOutletAsync()
        {
            try
            {
                await LoadRuntimeSettingsAsync(CancellationToken.None);
                await LoadAllTemplatesAsync(CancellationToken.None);

                await BuildSalePreviewAsync(CancellationToken.None);
                await BuildSaleReturnPreviewAsync(CancellationToken.None);
                await BuildVoucherPreviewAsync(CancellationToken.None);
                await BuildZReportPreviewAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Failed to load outlet template: " + ex.Message, "Receipt Builder");
            }
        }
    }
}
