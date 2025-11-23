// Pos.Client.Wpf/Windows/Settings/InvoiceSettingsViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Messages;
using Pos.Client.Wpf.Services;
using Pos.Domain.Entities; // Outlet
using Pos.Domain.Services; // IInvoiceSettingsLocalService, IInvoiceSettingsScopedService, IBankAccountService, IOutletService, ITerminalContext
using Pos.Domain.Settings; // InvoiceSettingsLocal, InvoiceSettingsScoped, DefaultBarcodeType

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class InvoiceSettingsViewModel : ObservableObject
    {
        private readonly IInvoiceSettingsLocalService _localSvc;
        private readonly IInvoiceSettingsScopedService _scopedSvc;
        private readonly IBankAccountService _banks;
        private readonly IOutletService _outlets;
        private readonly ITerminalContext _ctx;
        private readonly IDialogService? _dialog;

        public InvoiceSettingsViewModel(
            IInvoiceSettingsLocalService localSvc,
            IInvoiceSettingsScopedService scopedSvc,
            IBankAccountService banks,
            IOutletService outlets,
            ITerminalContext ctx,
            IDialogService? dialog = null)
        {
            _localSvc = localSvc;
            _scopedSvc = scopedSvc;
            _banks = banks;
            _outlets = outlets;
            _ctx = ctx;
            _dialog = dialog;

            // Load installed printers
            try
            {
                foreach (string p in PrinterSettings.InstalledPrinters)
                {
                    Printers.Add(p);
                    LabelPrinters.Add(p);
                }
            }
            catch { /* ignore */ }

            // Windows display time zones
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
                TimeZones.Add(tz.Id);

            _ = InitAsync();
        }

        // ===== Scope =====
        [ObservableProperty] private bool isGlobal = true;
        [ObservableProperty] private bool canEditGlobal = true; // wire to AuthZ if needed
        [ObservableProperty] private bool canEdit = true;       // wire to AuthZ if needed

        [ObservableProperty] private ObservableCollection<Outlet> outletsList = new();
        [ObservableProperty] private Outlet? selectedOutlet;

        // ===== Counter (local) =====
        [ObservableProperty] private int counterId;

        // ===== Local: Printers only =====
        [ObservableProperty] private ObservableCollection<string> printers = new();
        [ObservableProperty] private string? printerName;

        [ObservableProperty] private ObservableCollection<string> labelPrinters = new();
        [ObservableProperty] private string? labelPrinterName;

        // ===== Scoped: Display Timezone =====
        [ObservableProperty] private ObservableCollection<string> timeZones = new();
        [ObservableProperty] private string? displayTimeZoneId;

        // ===== Bank Accounts (shared list) =====
        public sealed record BankAccountPick(int AccountId, int BankAccountId, string Display);
        [ObservableProperty] private ObservableCollection<BankAccountPick> bankAccounts = new();

        [ObservableProperty] private int? salesCardClearingAccountId;
        [ObservableProperty] private int? purchaseBankAccountId;

        // ===== Items default barcode =====
        public Array BarcodeTypeValues { get; } = Enum.GetValues(typeof(DefaultBarcodeType));
        [ObservableProperty] private DefaultBarcodeType defaultBarcodeType = DefaultBarcodeType.Ean13;

        // ===== Print behavior / Drawer (scoped) =====
        [ObservableProperty] private bool cashDrawerKickEnabled;
        [ObservableProperty] private bool autoPrintOnSave;
        [ObservableProperty] private bool askBeforePrint = true;

        // ===== Footers (scoped) =====
        [ObservableProperty] private string? footerSale;
        [ObservableProperty] private string? footerSaleReturn;
        [ObservableProperty] private string? footerVoucher;
        [ObservableProperty] private string? footerZReport;

        // ===== Backups (scoped) =====
        
        // ===== Till requirement (scoped) =====
        [ObservableProperty] private bool useTill = true;
        private InvoiceSettingsScoped? _loadedScoped;

        // ===== Lifecycle =====
        private async Task InitAsync(CancellationToken ct = default)
        {
            // Load outlets for scope selector
            var outs = await _outlets.GetAllAsync(ct);
            OutletsList = new ObservableCollection<Outlet>(outs.OrderBy(o => o.Name));

            // Pick scope defaults: prefer current outlet
            SelectedOutlet = OutletsList.FirstOrDefault(o => o.Id == _ctx.OutletId) ?? OutletsList.FirstOrDefault();
            IsGlobal = true; // default to Global first (like Identity page)

            // Resolve current counter for local settings
            int counter = _ctx.CounterId;
            CounterId = counter;

            await LoadScopedAsync(ct);
            await LoadLocalAsync(ct);
            await LoadBankAccountsAsync(ct);
        }

        // ----- Loaders -----
        private async Task LoadBankAccountsAsync(CancellationToken ct)
        {
            try
            {
                var bankDtos = await _banks.SearchAsync(null, ct);
                BankAccounts = new ObservableCollection<BankAccountPick>(
                    bankDtos.Where(b => b.IsActive)
                            .Select(b => new BankAccountPick(b.AccountId, b.Id, $"{b.Code} — {b.Name} ({b.BankName})"))
                );
            }
            catch
            {
                BankAccounts = new ObservableCollection<BankAccountPick>();
            }
        }

        private async Task LoadLocalAsync(CancellationToken ct)
        {
            if (CounterId <= 0) return;
            var local = await _localSvc.GetForCounterAsync(CounterId, ct);

            PrinterName = FallbackPick(local.PrinterName, Printers);
            LabelPrinterName = FallbackPick(local.LabelPrinterName, LabelPrinters);
        }

        private async Task LoadScopedAsync(CancellationToken ct)
        {
            InvoiceSettingsScoped scoped = IsGlobal
                ? await _scopedSvc.GetGlobalAsync(ct)
                : await _scopedSvc.GetForOutletAsync(SelectedOutlet!.Id, ct);

            _loadedScoped = scoped;

            CashDrawerKickEnabled = scoped.CashDrawerKickEnabled;
            AutoPrintOnSave = scoped.AutoPrintOnSave;
            AskBeforePrint = scoped.AskBeforePrint;

            DisplayTimeZoneId = string.IsNullOrWhiteSpace(scoped.DisplayTimeZoneId)
                ? TimeZones.FirstOrDefault()
                : scoped.DisplayTimeZoneId;

            SalesCardClearingAccountId = scoped.SalesCardClearingAccountId;
            PurchaseBankAccountId = scoped.PurchaseBankAccountId;

            DefaultBarcodeType = scoped.DefaultBarcodeType;

            FooterSale = scoped.FooterSale;
            FooterSaleReturn = scoped.FooterSaleReturn;
            FooterVoucher = scoped.FooterVoucher;
            FooterZReport = scoped.FooterZReport;

            // Backup fields and BackupBaseFolder / UseServerForBackupRestore are NOT touched here.
            UseTill = scoped.UseTill;
        }


        private static string? FallbackPick(string? current, ObservableCollection<string> list)
            => string.IsNullOrWhiteSpace(current) ? list.FirstOrDefault() : current;

        // ----- Save -----
        [RelayCommand]
        private async Task SaveAsync(CancellationToken ct)
        {
            // Save Scoped first
            var scoped = new InvoiceSettingsScoped
            {
                OutletId = IsGlobal ? (int?)null : SelectedOutlet?.Id,

                CashDrawerKickEnabled = CashDrawerKickEnabled,
                AutoPrintOnSave = AutoPrintOnSave,
                AskBeforePrint = AskBeforePrint,

                DisplayTimeZoneId = DisplayTimeZoneId,

                SalesCardClearingAccountId = SalesCardClearingAccountId,
                PurchaseBankAccountId = PurchaseBankAccountId,

                DefaultBarcodeType = DefaultBarcodeType,

                FooterSale = FooterSale,
                FooterSaleReturn = FooterSaleReturn,
                FooterVoucher = FooterVoucher,
                FooterZReport = FooterZReport,

              
                UseTill = UseTill,

                UpdatedAtUtc = DateTime.UtcNow
            };

            await _scopedSvc.UpsertAsync(scoped, ct);

            // Save Local printers (per-counter)
            if (CounterId > 0)
            {
                var local = new InvoiceSettingsLocal
                {
                    CounterId = CounterId,
                    PrinterName = PrinterName,
                    LabelPrinterName = LabelPrinterName,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await _localSvc.UpsertAsync(local, ct);
                WeakReferenceMessenger.Default.Send(
    new InvoicePrintersChanged(
        CounterId: _ctx.CounterId,
        OutletId: _ctx.OutletId,
        ReceiptPrinter: local.PrinterName,        // ESC/POS invoice printer
        LabelPrinter: local.LabelPrinterName    // Label/TSC printer
    ));
            }

            MessageBox.Show("Invoice settings saved.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Scope react
        partial void OnIsGlobalChanged(bool value)
        {
            _ = ReloadScopeAsync();
        }

        partial void OnSelectedOutletChanged(Outlet? oldValue, Outlet? newValue)
        {
            if (!IsGlobal && newValue != null)
                _ = ReloadScopeAsync();
        }

        private async Task ReloadScopeAsync()
        {
            try { await LoadScopedAsync(CancellationToken.None); }
            catch { /* toast if you want */ }
        }

        [RelayCommand]
        private void UseWindowsDefaultPrinter()
        {
            try { using var doc = new PrintDocument(); PrinterName = doc.PrinterSettings.PrinterName; }
            catch { /* ignore */ }
        }
    }
}
