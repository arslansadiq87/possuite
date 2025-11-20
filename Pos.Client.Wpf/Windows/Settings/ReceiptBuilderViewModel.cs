// Pos.Client.Wpf/Windows/Settings/ReceiptBuilderViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Models;                    // CartLine
using Pos.Client.Wpf.Printing;                 // ReceiptPreviewBuilder, IRawPrinterService, EscPosReceiptBuilder
using Pos.Client.Wpf.Services;                 // IDialogService
using Pos.Domain.Entities;                     // ReceiptTemplate, Outlet, Sale, IdentitySettings
using Pos.Domain.Services;                     // IReceiptTemplateService, IOutletService, IIdentitySettingsService, IInvoiceSettingsLocalService, ITerminalContext
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
        [ObservableProperty] private ReceiptTemplate saleTemplate = new();
        [ObservableProperty] private string previewText = "";
        // Read-only display of the printer selected in Invoice Settings
        [ObservableProperty] private string? invoicePrinterName;
        // Enabled by General Settings (read-only flags for UI enable/disable)
        [ObservableProperty] private bool runtimeEnableFbr; // true if general settings enable FBR
        [ObservableProperty] private bool runtimeHasNtn;    // true if general settings have a non-empty NTN

        // Logo bytes from Identity/General (XAML uses BytesToBitmap converter)
        [ObservableProperty] private byte[]? identityLogoBytes;

        // ----- Init -----
        public async Task InitAsync(CancellationToken ct = default)
        {
            var outlets = await _outlets.GetAllAsync(ct);
            OutletChoices = new ObservableCollection<Outlet>(outlets.OrderBy(o => o.Name));

            // prefer the current terminal’s outlet
            SelectedOutlet = OutletChoices.FirstOrDefault(o => o.Id == _ctx.OutletId)
                             ?? OutletChoices.FirstOrDefault();

            await LoadSaleTemplateAsync(ct);
            await LoadRuntimeSettingsAsync(ct);
            await BuildSalePreviewAsync(ct);
        }

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



        // ----- Commands -----
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

        [RelayCommand]
        private async Task PreviewSaleAsync()
        {
            await BuildSalePreviewAsync();
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

        // ----- Preview builder -----
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
                QrText = tpl.ShowQr ? "https://example.com/e/INV-12345" : null
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

        // React to outlet switching
        partial void OnSelectedOutletChanged(Outlet? oldValue, Outlet? newValue)
        {
            _ = RefreshForOutletAsync();
        }

        private async Task RefreshForOutletAsync()
        {
            try
            {
                await LoadSaleTemplateAsync(CancellationToken.None);
                await LoadRuntimeSettingsAsync(CancellationToken.None);
                await BuildSalePreviewAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync("Failed to load outlet template: " + ex.Message, "Receipt Builder");
            }
        }
    }
}
