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
using Pos.Client.Wpf.Services;        // AppState / AppCtx
using Pos.Domain.Entities;
using Pos.Domain.Services;            // IInvoiceSettingsLocalService, IBankAccountService
using Pos.Domain.Settings;            // InvoiceSettingsLocal, DefaultBarcodeType

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class InvoiceSettingsViewModel : ObservableObject
    {
        private readonly IInvoiceSettingsLocalService _svc;
        private readonly IBankAccountService _banks;
        private readonly IDialogService? _dialog;            // NEW

        public InvoiceSettingsViewModel(
            IInvoiceSettingsLocalService svc,
            IBankAccountService banks, IDialogService? dialog = null)
        {
            _svc = svc;
            _banks = banks;
            _dialog = dialog;

            // Load installed printers (invoice + label)
            try
            {
                foreach (string p in PrinterSettings.InstalledPrinters)
                {
                    Printers.Add(p);
                    LabelPrinters.Add(p);
                }
            }
            catch
            {
                // Spooler not ready or no printers installed; ignore
            }

            // Load display time zones (Windows IDs)
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
                TimeZones.Add(tz.Id);

            // Fire-and-forget load
            _ = LoadAsync();
        }

        

        // ---------- Scope ----------
        [ObservableProperty] private int _counterId;

        // ---------- Printers ----------
        [ObservableProperty] private ObservableCollection<string> _printers = new();
        [ObservableProperty] private string? _printerName;

        // Label printer
        [ObservableProperty] private ObservableCollection<string> _labelPrinters = new();
        [ObservableProperty] private string? _labelPrinterName;

        // ---------- Display Timezone ----------
        [ObservableProperty] private ObservableCollection<string> _timeZones = new();
        [ObservableProperty] private string? _displayTimeZoneId;

        // ---------- Bank Accounts (both pickers share this list) ----------
        public sealed record BankAccountPick(int AccountId, int BankAccountId, string Display);

        [ObservableProperty] private ObservableCollection<BankAccountPick> _bankAccounts = new();

        // Selected values (store AccountId for both)
        [ObservableProperty] private int? _salesCardClearingAccountId;
        [ObservableProperty] private int? _purchaseBankAccountId;

        // ---------- Items default barcode ----------
        public Array BarcodeTypeValues { get; } = Enum.GetValues(typeof(DefaultBarcodeType));
        [ObservableProperty] private DefaultBarcodeType _defaultBarcodeType = DefaultBarcodeType.Ean13;

        // ---------- Print behavior / Drawer ----------
        [ObservableProperty] private bool _cashDrawerKickEnabled;
        [ObservableProperty] private bool _autoPrintOnSave;
        [ObservableProperty] private bool _askBeforePrint = true; // safe default

        // ---------- Footers ----------
        [ObservableProperty] private string? _footerSale;
        [ObservableProperty] private string? _footerSaleReturn;
        [ObservableProperty] private string? _footerVoucher;
        [ObservableProperty] private string? _footerZReport;
        // Backups
        [ObservableProperty] private bool _enableDailyBackup;   // NEW
        [ObservableProperty] private bool _enableHourlyBackup;  // NEW
        [ObservableProperty] private bool _useTill;


        // ---------- Load ----------
        private async Task LoadAsync(CancellationToken ct = default)
        {
            // Resolve current counter; do not throw at startup
            int counter = AppState.Current?.CurrentCounterId ?? 0;
            try
            {
                var (_, c) = AppCtx.GetOutletCounterOrThrow();
                counter = c;
            }
            catch { /* ignore until user assigns counter */ }

            CounterId = counter;
            if (CounterId <= 0) return;

            // Bank accounts list (used for both sales clearing & purchase bank)
            try
            {
                var bankDtos = await _banks.SearchAsync(null, ct); // all bank accounts
                BankAccounts = new ObservableCollection<BankAccountPick>(
                    bankDtos
                        .Where(b => b.IsActive)
                        .Select(b => new BankAccountPick(
                            b.AccountId,                                      // AccountId (stored in settings)
                            b.Id,                                             // BankAccount row id (for later use if needed)
                            $"{b.Code} — {b.Name} ({b.BankName})"
                        ))
                );
            }
            catch
            {
                // If lookup fails, keep list empty; user can retry later
                BankAccounts = new ObservableCollection<BankAccountPick>();
            }

            // Load saved settings
            var m = await _svc.GetForCounterAsync(CounterId, ct);

            PrinterName = FallbackPick(m.PrinterName, Printers);
            LabelPrinterName = FallbackPick(m.LabelPrinterName, LabelPrinters);
            DisplayTimeZoneId = string.IsNullOrWhiteSpace(m.DisplayTimeZoneId)
                ? TimeZones.FirstOrDefault()
                : m.DisplayTimeZoneId;

            CashDrawerKickEnabled = m.CashDrawerKickEnabled;
            AutoPrintOnSave = m.AutoPrintOnSave;
            AskBeforePrint = m.AskBeforePrint;

            DefaultBarcodeType = m.DefaultBarcodeType;

            FooterSale = m.FooterSale;
            FooterSaleReturn = m.FooterSaleReturn;
            FooterVoucher = m.FooterVoucher;
            FooterZReport = m.FooterZReport;
            EnableDailyBackup = m.EnableDailyBackup;   // NEW
            EnableHourlyBackup = m.EnableHourlyBackup;  // NEW
            UseTill = m.UseTill;   // moved from Preferences

            SalesCardClearingAccountId = m.SalesCardClearingAccountId;  // AccountId
            PurchaseBankAccountId = m.PurchaseBankAccountId;       // AccountId
        }

        private static string? FallbackPick(string? current, ObservableCollection<string> list)
            => string.IsNullOrWhiteSpace(current) ? list.FirstOrDefault() : current;

        // ---------- Save ----------
        [RelayCommand]
        private async Task SaveAsync(CancellationToken ct)
        {
            if (CounterId <= 0) return;

            try
            {
                var model = new InvoiceSettingsLocal
                {
                    CounterId = CounterId,
                    PrinterName = PrinterName,
                    LabelPrinterName = LabelPrinterName,
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
                    EnableDailyBackup = EnableDailyBackup,    // NEW
                    EnableHourlyBackup = EnableHourlyBackup,    // NEW
                    UseTill = UseTill,

                };

                await _svc.UpsertAsync(model, ct);

                // ✅ Show success message box after save
                MessageBox.Show("Invoice settings saved successfully.",
                                "Saved",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                //// (Optional) Also show your dialog service toast, if you want both:
                //if (_dialog != null)
                //    await _dialog.ShowAsync("Invoice settings saved.", "Success");
            }
            finally
            {
                // no-op
            }
        }


        // ---------- Helpers ----------
        [RelayCommand]
        private void UseWindowsDefaultPrinter()
        {
            try
            {
                using var doc = new PrintDocument();
                PrinterName = doc.PrinterSettings.PrinterName;
            }
            catch
            {
                // Optional: dialog/toast
            }
        }
    }
}
